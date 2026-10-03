using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>AST-scoped, opt-in FROM/INTO/JOIN whitespace policies for top-level SELECT.</summary>
internal static class SelectFromLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("select.from.", StringComparison.Ordinal)
            || key.StartsWith("select.into.", StringComparison.Ordinal)
            || key.StartsWith("select.join.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is not TSqlScript script) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        foreach (var select in SelectQueryScope.Statements(script, options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (select.QueryExpression is QuerySpecification query) ProcessInto(select, query);
        }
        foreach (var query in SelectQueryScope.Queries(script, options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessFrom(query);
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;

        void ProcessInto(SelectStatement select, QuerySpecification query)
        {
            if (select.Into is null || query.SelectElements.Count == 0) return;
            var last = query.SelectElements[query.SelectElements.Count - 1];
            var keyword = editor.Find(last.StartOffset + last.FragmentLength, select.Into.StartOffset,
                token => token.Text.Equals("INTO", StringComparison.OrdinalIgnoreCase));
            if (keyword is null) return;
            editor.Before(keyword.Offset, Break("select.into.breakBefore"), Indent("select.into.keywordIndent"));
            editor.Before(select.Into.StartOffset, Break("select.into.breakAfter"), Indent("select.into.tableIndent"));
        }

        void ProcessFrom(QuerySpecification query)
        {
            var from = query.FromClause;
            if (from is null || from.TableReferences.Count == 0) return;
            var keyword = editor.Find(from.StartOffset, from.TableReferences[0].StartOffset,
                token => token.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase));
            if (keyword is null) return;
            editor.Before(keyword.Offset, Break("select.from.breakBefore"), Indent("select.from.keywordIndent"));
            editor.Before(from.TableReferences[0].StartOffset, Break("select.from.breakAfter"),
                Indent("select.from.listIndent"));
            var stack = NativeRules.Get(options, "select.from.stackList").Choice;
            if (stack != "inherit")
            {
                for (var index = 1; index < from.TableReferences.Count; index++)
                {
                    var previous = from.TableReferences[index - 1];
                    var next = from.TableReferences[index];
                    var comma = editor.Find(previous.StartOffset + previous.FragmentLength,
                        next.StartOffset, token => token.Text == ",");
                    if (comma is null) continue;
                    var mode = NativeRules.Get(options, "select.from.stackMode").Choice;
                    var desired = stack == "off" || mode == "auto"
                        && next.StartOffset - from.StartOffset < options.General.MaxLineWidth
                        ? "never" : "always";
                    editor.After(comma.Offset, desired, Indent("select.from.listIndent"));
                }
            }
            foreach (var table in from.TableReferences) ProcessJoinTree(table);
        }

        void ProcessJoinTree(TableReference table)
        {
            if (table is not JoinTableReference join || join.FirstTableReference is null
                || join.SecondTableReference is null) return;
            ProcessJoinTree(join.FirstTableReference);
            ProcessJoinTree(join.SecondTableReference);
            var first = join.FirstTableReference;
            var second = join.SecondTableReference;
            var firstEnd = first.StartOffset + first.FragmentLength;
            var keyword = editor.Find(firstEnd, second.StartOffset,
                token => token.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("APPLY", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("INNER", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("LEFT", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("FULL", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("CROSS", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("OUTER", StringComparison.OrdinalIgnoreCase));
            var finalKeyword = editor.FindLast(firstEnd, second.StartOffset,
                token => token.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("APPLY", StringComparison.OrdinalIgnoreCase));
            if (keyword is null || finalKeyword is null) return;
            editor.Before(keyword.Offset, Break("select.join.breakBefore"), Indent("select.join.keywordIndent"));
            editor.Before(second.StartOffset, Break("select.join.breakAfter"), Indent("select.join.tableIndent"));
            if (join is not QualifiedJoin { SearchCondition: { } condition }) return;
            var secondEnd = second.StartOffset + second.FragmentLength;
            var on = editor.Find(secondEnd, condition.StartOffset,
                token => token.Text.Equals("ON", StringComparison.OrdinalIgnoreCase));
            if (on is null) return;
            editor.Before(on.Offset, Break("select.join.onBreakBefore"), Indent("select.join.onKeywordIndent"));
            editor.Before(condition.StartOffset, Break("select.join.onBreakAfter"),
                Indent("select.join.onConditionIndent"));
            SqlBooleanPolicy.Apply(condition, editor, options, "select.join");
        }

        string Break(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, key).Indent;
    }
}
