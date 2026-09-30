using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Opt-in, AST-scoped UPDATE/DELETE policies; never rewrites a non-whitespace token.</summary>
internal static class UpdateDeleteLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("update.", StringComparison.Ordinal)
            || key.StartsWith("delete.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        foreach (var statement in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken)
                     .OfType<StatementWithCtesAndXmlNamespaces>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement is UpdateStatement { UpdateSpecification: { } update })
                Process(statement, update, update.FromClause, update.WhereClause, "update");
            else if (statement is DeleteStatement { DeleteSpecification: { } delete })
                Process(statement, delete, delete.FromClause, delete.WhereClause, "delete");
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var after = parser.Parse(changed, dialect, cancellationToken);
        return after.ParseSucceeded && SqlSpacing.SameTokens(parsed, after) ? changed : source;

        void Process(StatementWithCtesAndXmlNamespaces statement, DataModificationSpecification spec,
            FromClause? from, WhereClause? where, string prefix)
        {
            var anchor = spec.StartOffset;
            if (spec.Target is null) return;
            if (prefix == "delete")
            {
                var headerFrom = editor.Find(spec.TopRowFilter is { } top ? End(top) : anchor,
                    spec.Target.StartOffset, Is("FROM"));
                if (headerFrom is not null)
                    Boundary(headerFrom.Offset, "delete.target.breakBeforeFrom", "delete.target.fromKeywordIndent", anchor);
            }
            Boundary(spec.Target.StartOffset, prefix + ".target.breakBefore", prefix + ".target.indent", anchor);
            if (spec is UpdateSpecification { SetClauses.Count: > 0 } update)
            {
                var items = update.SetClauses.Cast<TSqlFragment>().ToArray();
                var set = editor.Find(spec.Target.StartOffset + spec.Target.FragmentLength, items[0].StartOffset, Is("SET"));
                if (set is not null)
                {
                    Boundary(set.Offset, "update.set.breakBefore", "update.set.keywordIndent", anchor);
                    Boundary(items[0].StartOffset, "update.set.breakAfter", "update.set.listIndent", set.Offset);
                    var compact = List(items, "update.set", set.Offset, Fits(set.Offset, End(items[items.Length - 1]), anchor));
                    // Horizontal lists have no shared assignment column. Vertical lists retain legacy alignment.
                    if (compact)
                        foreach (var assignment in update.SetClauses.OfType<AssignmentSetClause>())
                            if (assignment.Column is { } column && assignment.NewValue is { } value)
                            {
                                var equals = editor.Find(End(column), value.StartOffset, token => token.Text == "=");
                                if (equals is not null) editor.Before(equals.Offset, "inherit", spaceMode: "insert");
                            }
                }
            }
            if (spec.OutputIntoClause is { } outputInto)
                Output(outputInto, outputInto.SelectColumns.Cast<TSqlFragment>().ToArray(), prefix, anchor);
            if (spec.OutputClause is { } output)
                Output(output, output.SelectColumns.Cast<TSqlFragment>().ToArray(), prefix, anchor);
            var inherited = NativeRules.Get(options, prefix + ".from.useSelectFormatting").Boolean;
            var fromPrefix = inherited ? "select.from" : prefix + ".from";
            var joinPrefix = inherited ? "select.join" : prefix + ".join";
            if (from is { TableReferences.Count: > 0 })
            {
                var keyword = editor.Find(from.StartOffset, from.TableReferences[0].StartOffset, Is("FROM"));
                if (keyword is not null)
                {
                    Boundary(keyword.Offset, fromPrefix + ".breakBefore", fromPrefix + ".keywordIndent", anchor);
                    Boundary(from.TableReferences[0].StartOffset, fromPrefix + ".breakAfter", fromPrefix + ".listIndent", keyword.Offset);
                    List(from.TableReferences.Cast<TSqlFragment>().ToArray(), fromPrefix, keyword.Offset,
                        Fits(from.StartOffset, End(from), anchor));
                }
                foreach (var table in from.TableReferences) Join(table, joinPrefix, anchor);
            }
            if (where is not null)
            {
                var first = (TSqlFragment?)where.SearchCondition ?? where.Cursor;
                var keyword = first is null ? null : editor.Find(where.StartOffset, first.StartOffset, Is("WHERE"));
                if (keyword is not null && first is not null)
                {
                    Boundary(keyword.Offset, prefix + ".where.breakBefore", prefix + ".where.keywordIndent", anchor);
                    var conditionStart = where.SearchCondition is null
                        ? editor.Find(keyword.Offset + keyword.Text.Length, first.StartOffset, Is("CURRENT"))?.Offset
                            ?? first.StartOffset
                        : first.StartOffset;
                    Boundary(conditionStart, prefix + ".where.breakAfter", prefix + ".where.conditionIndent", keyword.Offset);
                    if (where.SearchCondition is { } condition)
                        SqlBooleanPolicy.Apply(condition, editor, options, prefix + ".where", keyword.Offset);
                }
            }
            if (statement.OptimizerHints.Count > 0)
            {
                var first = statement.OptimizerHints[0];
                var keyword = editor.Find(End(spec), first.StartOffset, Is("OPTION"));
                var open = keyword is null ? null : editor.Find(keyword.Offset + keyword.Text.Length, first.StartOffset,
                    token => token.Text == "(");
                if (keyword is not null && open is not null)
                {
                    Boundary(keyword.Offset, prefix + ".option.breakBefore", prefix + ".option.keywordIndent", anchor);
                    Boundary(open.Offset, prefix + ".option.breakAfter", prefix + ".option.hintsIndent", keyword.Offset);
                    foreach (var hint in statement.OptimizerHints)
                        editor.Before(hint.StartOffset, "inherit", Indent(prefix + ".option.hintsIndent"), anchorOffset: keyword.Offset);
                }
            }
        }

        void Output(TSqlFragment clause, TSqlFragment[] items, string prefix, int anchor)
        {
            if (items.Length == 0) return;
            var keyword = editor.Find(clause.StartOffset, items[0].StartOffset, Is("OUTPUT"));
            if (keyword is null) return;
            var group = prefix + ".output";
            Boundary(keyword.Offset, group + ".breakBefore", group + ".keywordIndent", anchor);
            Boundary(items[0].StartOffset, group + ".breakAfter", group + ".listIndent", keyword.Offset);
            List(items, group, keyword.Offset, Fits(clause.StartOffset, End(items[items.Length - 1]), anchor));
        }

        void Join(TableReference table, string prefix, int anchor)
        {
            if (table is JoinParenthesisTableReference { Join: { } parenthesized })
            {
                Join(parenthesized, prefix, anchor);
                return;
            }
            if (table is not JoinTableReference { FirstTableReference: { } first, SecondTableReference: { } second } join)
                return;
            Join(first, prefix, anchor);
            Join(second, prefix, anchor);
            var keyword = editor.Find(End(first), second.StartOffset, token => new[]
                { "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "LOOP", "HASH", "MERGE", "REMOTE", "JOIN", "APPLY" }
                .Contains(token.Text, StringComparer.OrdinalIgnoreCase));
            if (keyword is null) return;
            Boundary(keyword.Offset, prefix + ".breakBefore", prefix + ".keywordIndent", anchor);
            Boundary(second.StartOffset, prefix + ".breakAfter", prefix + ".tableIndent", keyword.Offset);
            if (join is not QualifiedJoin { SearchCondition: { } condition }) return;
            var on = editor.Find(End(second), condition.StartOffset, Is("ON"));
            if (on is null) return;
            Boundary(on.Offset, prefix + ".onBreakBefore", prefix + ".onKeywordIndent", keyword.Offset);
            Boundary(condition.StartOffset, prefix + ".onBreakAfter", prefix + ".onConditionIndent", on.Offset);
            SqlBooleanPolicy.Apply(condition, editor, options, prefix, on.Offset);
        }

        bool List(TSqlFragment[] items, string prefix, int anchor, bool fits)
        {
            var stack = Choice(prefix + ".stackList");
            var indent = Indent(prefix + ".listIndent");
            var compact = stack == "off" || stack == "on" && Choice(prefix + ".stackMode") == "auto" && fits;
            if (stack == "inherit" && !indent.Enabled) return false;
            var mode = compact ? "never" : "always";
            var leading = Choice("stackedList.commaPlacement") == "leading" && mode == "always";
            var listAnchor = indent.Enabled ? anchor : items[0].StartOffset;
            for (var index = 1; index < items.Length; index++)
            {
                var comma = editor.Find(End(items[index - 1]), items[index].StartOffset, token => token.Text == ",");
                if (comma is null) continue;
                if (stack == "inherit")
                {
                    var previousEnd = End(items[index - 1]);
                    var commaIsLeading = source.Substring(previousEnd, comma.Offset - previousEnd)
                        .IndexOfAny(new[] { '\r', '\n' }) >= 0;
                    if (commaIsLeading)
                        editor.Before(comma.Offset, "inherit", indent, anchorOffset: listAnchor);
                    else editor.After(comma.Offset, "inherit", indent, anchorOffset: listAnchor);
                    continue;
                }
                editor.Before(comma.Offset, leading ? mode : "never", leading ? indent : null,
                    leading ? "inherit" : Choice("spacing.beforeComma") == "insert" ? "insert" : "remove", listAnchor);
                editor.After(comma.Offset, leading ? "never" : mode, leading ? null : indent,
                    leading ? Choice("stackedList.spaceAfterLeadingComma") : "inherit", listAnchor);
            }
            return compact;
        }

        bool Fits(int start, int end, int anchor)
        {
            var line = source.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, anchor - 1)) + 1;
            var width = 0;
            while (line < source.Length && source[line] is ' ' or '\t')
                width += source[line++] == '\t' ? options.Indent.Size : 1;
            return width + Regex.Replace(source.Substring(start, end - start), @"[ \t]*(?:\r\n|\r|\n)[ \t]*", " ").Length
                <= options.General.MaxLineWidth;
        }
        void Boundary(int offset, string breakKey, string indentKey, int anchor) =>
            editor.Before(offset, Choice(breakKey), Indent(indentKey), anchorOffset: anchor);
        string Choice(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, key).Indent;
    }

    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token =>
        token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
