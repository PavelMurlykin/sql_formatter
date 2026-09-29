using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>CTE, FOR, OPTION, and legacy COMPUTE boundary policies for SELECT.</summary>
internal static class SelectTailLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("select.cte.", StringComparison.Ordinal)
            || key.StartsWith("select.for.", StringComparison.Ordinal)
            || key.StartsWith("select.option.", StringComparison.Ordinal)
            || key.StartsWith("select.compute.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is not TSqlScript script) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        foreach (var statement in script.Batches.SelectMany(batch => batch.Statements).OfType<SelectStatement>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessCtes(statement);
            ProcessFor(statement);
            ProcessOption(statement);
            ProcessCompute(statement);
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;

        void ProcessCtes(SelectStatement statement)
        {
            var with = statement.WithCtesAndXmlNamespaces;
            if (with is null || with.CommonTableExpressions.Count == 0) return;
            var firstCte = with.CommonTableExpressions[0];
            var withToken = editor.Find(with.StartOffset, firstCte.StartOffset,
                token => token.Text.Equals("WITH", StringComparison.OrdinalIgnoreCase));
            foreach (var cte in with.CommonTableExpressions)
            {
                if (cte.ExpressionName is null || cte.QueryExpression is null) continue;
                editor.Before(cte.ExpressionName.StartOffset,
                    cte == firstCte ? Break("select.cte.breakAfterWith") : "inherit",
                    Indent("select.cte.expressionIndent"));
                var afterName = cte.ExpressionName.StartOffset + cte.ExpressionName.FragmentLength;
                if (cte.Columns.Count > 0)
                {
                    var open = editor.Find(afterName, cte.Columns[0].StartOffset, token => token.Text == "(");
                    var last = cte.Columns[cte.Columns.Count - 1];
                    var close = editor.Find(last.StartOffset + last.FragmentLength,
                        cte.QueryExpression.StartOffset, token => token.Text == ")");
                    if (open is not null && close is not null)
                    {
                        editor.Before(open.Offset, Break("select.cte.breakBeforeColumnOpen"),
                            Indent("select.cte.columnBraceIndent"));
                        editor.Before(cte.Columns[0].StartOffset, Break("select.cte.breakAfterColumnOpen"),
                            Indent("select.cte.columnListIndent"));
                        editor.Before(close.Offset, Break("select.cte.breakBeforeColumnClose"),
                            Indent("select.cte.columnBraceIndent"));
                        var stack = NativeRules.Get(options, "select.cte.stackColumns").Choice;
                        var mode = NativeRules.Get(options, "select.cte.stackMode").Choice;
                        if (stack != "inherit")
                        {
                            for (var index = 1; index < cte.Columns.Count; index++)
                            {
                                var previous = cte.Columns[index - 1];
                                var next = cte.Columns[index];
                                var comma = editor.Find(previous.StartOffset + previous.FragmentLength,
                                    next.StartOffset, token => token.Text == ",");
                                if (comma is null) continue;
                                var desired = stack == "off" || mode == "auto"
                                    && next.StartOffset - open.Offset < options.General.MaxLineWidth
                                    ? "never" : "always";
                                editor.After(comma.Offset, desired, Indent("select.cte.columnListIndent"));
                            }
                        }
                    }
                }
                var asToken = editor.Find(afterName, cte.QueryExpression.StartOffset,
                    token => token.Text.Equals("AS", StringComparison.OrdinalIgnoreCase));
                if (asToken is null) continue;
                editor.Before(asToken.Offset, Break("select.cte.breakBeforeAs"));
                var subqueryOpen = editor.Find(asToken.Offset + asToken.Text.Length,
                    cte.QueryExpression.StartOffset, token => token.Text == "(");
                if (subqueryOpen is not null)
                    editor.Before(subqueryOpen.Offset, Break("select.cte.breakAfterAs"),
                        Indent("select.cte.subqueryBraceIndent"));
                var queryEnd = cte.QueryExpression.StartOffset + cte.QueryExpression.FragmentLength;
                var subqueryClose = editor.Find(queryEnd, cte.StartOffset + cte.FragmentLength,
                    token => token.Text == ")");
                if (subqueryClose is not null)
                    editor.Before(subqueryClose.Offset, "inherit", Indent("select.cte.subqueryBraceIndent"));
            }
            if (withToken is null) return;
        }

        void ProcessFor(SelectStatement statement)
        {
            if (statement.QueryExpression is not QuerySpecification { ForClause: { } clause }) return;
            var end = clause.StartOffset + clause.FragmentLength;
            var forToken = editor.Find(clause.StartOffset, end,
                token => token.Text.Equals("FOR", StringComparison.OrdinalIgnoreCase));
            if (forToken is null) return;
            editor.Before(forToken.Offset, Break("select.for.breakBefore"), Indent("select.for.keywordIndent"));
            var xml = editor.Find(forToken.Offset + forToken.Text.Length, end,
                token => token.Text.Equals("XML", StringComparison.OrdinalIgnoreCase));
            if (xml is not null)
                editor.After(xml.Offset, Break("select.for.breakAfterXml"), Indent("select.for.specIndent"));
        }

        void ProcessOption(SelectStatement statement)
        {
            if (statement.OptimizerHints.Count == 0 || statement.QueryExpression is null) return;
            var hint = statement.OptimizerHints[0];
            var queryEnd = statement.QueryExpression.StartOffset + statement.QueryExpression.FragmentLength;
            var option = editor.Find(queryEnd, hint.StartOffset,
                token => token.Text.Equals("OPTION", StringComparison.OrdinalIgnoreCase));
            if (option is null) return;
            editor.Before(option.Offset, Break("select.option.breakBefore"), Indent("select.option.keywordIndent"));
            var open = editor.Find(option.Offset + option.Text.Length, hint.StartOffset,
                token => token.Text == "(");
            if (open is not null)
            {
                editor.Before(open.Offset, Break("select.option.breakAfter"));
                editor.Before(hint.StartOffset, "inherit", Indent("select.option.hintsIndent"));
            }
        }

        void ProcessCompute(SelectStatement statement)
        {
            if (parsed.ParserVersion != SqlVersion.Sql100) return;
            foreach (var clause in statement.ComputeClauses)
            {
                if (clause.ComputeFunctions.Count == 0) continue;
                var first = clause.ComputeFunctions[0];
                var keyword = editor.Find(clause.StartOffset, first.StartOffset,
                    token => token.Text.Equals("COMPUTE", StringComparison.OrdinalIgnoreCase));
                if (keyword is null) continue;
                editor.Before(keyword.Offset, Break("select.compute.breakBefore"),
                    Indent("select.compute.keywordIndent"));
                editor.After(keyword.Offset, Break("select.compute.breakAfter"),
                    Indent("select.compute.expressionIndent"));
            }
        }

        string Break(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, key).Indent;
    }
}
