using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Independent opt-in WHERE/HAVING/GROUP BY/ORDER BY layout for top-level SELECT.</summary>
internal static class SelectClauseLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("select.where.", StringComparison.Ordinal)
            || key.StartsWith("select.having.", StringComparison.Ordinal)
            || key.StartsWith("select.groupBy.", StringComparison.Ordinal)
            || key.StartsWith("select.orderBy.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is not TSqlScript script) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        foreach (var statement in script.Batches.SelectMany(batch => batch.Statements))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement is not SelectStatement { QueryExpression: QuerySpecification query }) continue;
            if (query.WhereClause?.SearchCondition is { } where)
                Condition(query.WhereClause, where, "select.where", "WHERE");
            if (query.HavingClause?.SearchCondition is { } having)
                Condition(query.HavingClause, having, "select.having", "HAVING");
            if (query.GroupByClause is { GroupingSpecifications.Count: > 0 } group)
                List(group, group.GroupingSpecifications.Cast<TSqlFragment>().ToArray(),
                    "select.groupBy", "GROUP");
            if (query.OrderByClause is { OrderByElements.Count: > 0 } order)
                List(order, order.OrderByElements.Cast<TSqlFragment>().ToArray(),
                    "select.orderBy", "ORDER");
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;

        void Condition(TSqlFragment clause, BooleanExpression expression, string prefix, string word)
        {
            var keyword = editor.Find(clause.StartOffset, expression.StartOffset,
                token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (keyword is null) return;
            editor.Before(keyword.Offset, Break(prefix + ".breakBefore"), Indent(prefix + ".keywordIndent"));
            editor.Before(expression.StartOffset, Break(prefix + ".breakAfter"),
                Indent(prefix + ".conditionIndent"));
            SqlBooleanPolicy.Apply(expression, editor, options, prefix);
        }

        void List(TSqlFragment clause, TSqlFragment[] items, string prefix, string word)
        {
            var first = editor.Find(clause.StartOffset, items[0].StartOffset,
                token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (first is null) return;
            editor.Before(first.Offset, Break(prefix + ".breakBefore"), Indent(prefix + ".keywordIndent"));
            editor.Before(items[0].StartOffset, Break(prefix + ".breakAfter"),
                Indent(prefix + ".listIndent"));
            var stack = NativeRules.Get(options, prefix + ".stackList").Choice;
            if (stack == "inherit") return;
            var mode = NativeRules.Get(options, prefix + ".stackMode").Choice;
            for (var index = 1; index < items.Length; index++)
            {
                var previous = items[index - 1];
                var next = items[index];
                var comma = editor.Find(previous.StartOffset + previous.FragmentLength,
                    next.StartOffset, token => token.Text == ",");
                if (comma is null) continue;
                var desired = stack == "off" || mode == "auto"
                    && next.StartOffset - clause.StartOffset < options.General.MaxLineWidth
                    ? "never" : "always";
                editor.After(comma.Offset, desired, Indent(prefix + ".listIndent"));
            }
        }

        string Break(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, key).Indent;
    }
}
