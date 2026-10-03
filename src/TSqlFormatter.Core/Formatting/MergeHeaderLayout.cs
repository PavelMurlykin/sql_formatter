using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Opt-in MERGE target/hints, source tree, and match predicate; actions are outside this policy.</summary>
internal static class MergeHeaderLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("merge.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var measuredGaps = new Dictionary<int, int>();
        var tokens = parsed.Tokens.Where(token => token.TokenType is not
            (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).ToArray();
        foreach (var statement in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken).OfType<MergeStatement>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement.MergeSpecification is not { Target: { } target, TableReference: { } table,
                    SearchCondition: { } condition } spec) continue;
            var anchor = spec.StartOffset;
            var into = editor.Find(spec.TopRowFilter is { } top ? End(top) : anchor, target.StartOffset, Is("INTO"));
            if (into is not null) Boundary(into.Offset, "into.breakBefore", "into.keywordIndent", anchor);
            Boundary(target.StartOffset, "into.breakBeforeTable", "into.tableIndent", anchor);
            if (target is NamedTableReference { TableHints.Count: > 0, SchemaObject: { } name } named)
                Hints(named, name, anchor);
            var usingKeyword = editor.Find(spec.TableAlias is { } alias ? End(alias) : End(target), table.StartOffset, Is("USING"));
            if (usingKeyword is null) continue;
            Boundary(usingKeyword.Offset, "using.breakBefore", "using.keywordIndent", anchor);
            editor.Before(table.StartOffset, Choice("using.breakAfter"), anchorOffset: usingKeyword.Offset);
            var joinPrefix = NativeRules.Get(options, "merge.join.useSelectFormatting").Boolean ? "select.join" : "merge.join";
            Source(table, joinPrefix, anchor, usingKeyword.Offset);
            var on = editor.Find(End(table), condition.StartOffset, Is("ON"));
            if (on is null) continue;
            Boundary(on.Offset, "on.breakBefore", "on.keywordIndent", anchor);
            Boundary(condition.StartOffset, "on.breakAfter", "on.conditionIndent", on.Offset);
            SqlBooleanPolicy.Apply(condition, editor, options, "merge.on", on.Offset);
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;

        void Hints(NamedTableReference target, SchemaObjectName name, int anchor)
        {
            var hints = target.TableHints;
            var with = editor.Find(End(name), hints[0].StartOffset, Is("WITH"));
            if (with is null) return;
            var open = editor.Find(with.Offset + with.Text.Length, hints[0].StartOffset, token => token.Text == "(");
            var close = editor.Find(End(hints[hints.Count - 1]), End(target), token => token.Text == ")");
            if (open is null || close is null) return;
            Boundary(with.Offset, "hints.breakBefore", "hints.keywordIndent", anchor);
            editor.Before(open.Offset, Choice("hints.breakBeforeOpen"), Indent("hints.braceIndent"),
                Choice("hints.spaceBeforeOpen"), anchor);
            editor.Before(hints[0].StartOffset, Choice("hints.breakAfterOpen"), Indent("hints.listIndent"),
                Choice("hints.spaceWithin"), open.Offset);
            foreach (var hint in hints.Skip(1))
                editor.Before(hint.StartOffset, "inherit", Indent("hints.listIndent"), anchorOffset: open.Offset);
            editor.Before(close.Offset, Choice("hints.breakBeforeClose"), Indent("hints.braceIndent"),
                Choice("hints.spaceWithin"), anchor);
        }

        void Source(TableReference table, string prefix, int anchor, int usingAnchor)
        {
            // Derived queries keep their subquery policies; VALUES in MERGE actions are not source rows.
            if (table is InlineDerivedTable { RowValues.Count: > 0 } inline)
            {
                Values(inline, usingAnchor);
                return;
            }
            if (table is JoinParenthesisTableReference { Join: { } parenthesized })
            {
                Source(parenthesized, prefix, anchor, usingAnchor);
                return;
            }
            if (table is not JoinTableReference { FirstTableReference: { } first, SecondTableReference: { } second } join)
                return;
            Source(first, prefix, anchor, usingAnchor);
            var keyword = editor.Find(End(first), second.StartOffset, token => new[]
                { "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "LOOP", "HASH", "MERGE", "REMOTE", "JOIN", "APPLY" }
                .Contains(token.Text, StringComparer.OrdinalIgnoreCase));
            if (keyword is null)
            {
                Source(second, prefix, anchor, usingAnchor);
                return;
            }
            editor.Before(keyword.Offset, FullChoice(prefix + ".breakBefore"), FullIndent(prefix + ".keywordIndent"), anchorOffset: anchor);
            editor.Before(second.StartOffset, FullChoice(prefix + ".breakAfter"), FullIndent(prefix + ".tableIndent"), anchorOffset: keyword.Offset);
            Source(second, prefix, anchor, usingAnchor);
            if (join is not QualifiedJoin { SearchCondition: { } condition }) return;
            var on = editor.Find(End(second), condition.StartOffset, Is("ON"));
            if (on is null) return;
            editor.Before(on.Offset, FullChoice(prefix + ".onBreakBefore"), FullIndent(prefix + ".onKeywordIndent"), anchorOffset: keyword.Offset);
            editor.Before(condition.StartOffset, FullChoice(prefix + ".onBreakAfter"), FullIndent(prefix + ".onConditionIndent"), anchorOffset: on.Offset);
            SqlBooleanPolicy.Apply(condition, editor, options, prefix, on.Offset);
        }

        void Values(InlineDerivedTable table, int anchor)
        {
            var keyword = editor.Find(table.StartOffset, table.RowValues[0].StartOffset, Is("VALUES"));
            if (keyword is null) return;
            Boundary(keyword.Offset, "values.breakBeforeKeyword", "values.keywordIndent", anchor);
            editor.Before(table.RowValues[0].StartOffset, Choice("values.breakAfterKeyword"), Indent("values.braceIndent"),
                Choice("values.spaceAfterKeyword"), anchor);
            measuredGaps[table.RowValues[0].StartOffset] = GapWidth(table.RowValues[0].StartOffset,
                "values.breakAfterKeyword", "values.spaceAfterKeyword", "values.braceIndent");
            MeasureList(table.RowValues.Cast<TSqlFragment>().ToArray(), "values.stackRows", "values.braceIndent");
            foreach (var row in table.RowValues)
            {
                if (row.ColumnValues.Count == 0) continue;
                var items = row.ColumnValues.Cast<TSqlFragment>().ToArray();
                var close = editor.Find(End(items[items.Length - 1]), End(row), token => token.Text == ")");
                measuredGaps[items[0].StartOffset] = GapWidth(items[0].StartOffset,
                    "values.breakAfterOpen", "values.spaceWithin", "values.listIndent");
                if (close is not null) measuredGaps[close.Offset] = GapWidth(close.Offset,
                    "values.breakBeforeClose", "values.spaceWithin", "values.braceIndent");
                MeasureList(items, "values.stackList", "values.listIndent");
            }
            // Establish row anchors before indenting their contents, including rows newly moved to a new line.
            List(table.RowValues.Cast<TSqlFragment>().ToArray(), "values.stackRows", "values.stackRowsMode",
                "values.braceIndent", anchor, Fits(keyword.Offset, End(table.RowValues[table.RowValues.Count - 1]), keyword.Offset));
            foreach (var row in table.RowValues)
            {
                if (row.ColumnValues.Count == 0) continue;
                var items = row.ColumnValues.Cast<TSqlFragment>().ToArray();
                var open = editor.Find(row.StartOffset, items[0].StartOffset, token => token.Text == "(");
                var close = editor.Find(End(items[items.Length - 1]), End(row), token => token.Text == ")");
                if (open is null || close is null) continue;
                editor.Before(items[0].StartOffset, Choice("values.breakAfterOpen"), Indent("values.listIndent"),
                    Choice("values.spaceWithin"), open.Offset);
                List(items, "values.stackList", "values.stackMode", "values.listIndent", open.Offset,
                    Fits(open.Offset, close.Offset + close.Text.Length, open.Offset));
                editor.Before(close.Offset, Choice("values.breakBeforeClose"), Indent("values.braceIndent"),
                    Choice("values.spaceWithin"), anchor);
            }
        }

        void List(TSqlFragment[] items, string stackKey, string modeKey, string indentKey, int anchor, bool fits)
        {
            var stack = Choice(stackKey);
            var indent = Indent(indentKey);
            if (stack == "inherit" && !indent.Enabled) return;
            var mode = stack == "off" || Choice(modeKey) == "auto" && fits ? "never" : "always";
            var leading = FullChoice("stackedList.commaPlacement") == "leading" && mode == "always";
            var listAnchor = indent.Enabled ? anchor : items[0].StartOffset;
            for (var index = 1; index < items.Length; index++)
            {
                var previousEnd = End(items[index - 1]);
                var comma = editor.Find(previousEnd, items[index].StartOffset, token => token.Text == ",");
                if (comma is null) continue;
                if (stack == "inherit")
                {
                    if (source.Substring(previousEnd, comma.Offset - previousEnd).IndexOfAny(new[] { '\r', '\n' }) >= 0)
                        editor.Before(comma.Offset, "inherit", indent, anchorOffset: listAnchor);
                    else editor.After(comma.Offset, "inherit", indent, anchorOffset: listAnchor);
                    continue;
                }
                editor.Before(comma.Offset, leading ? mode : "never", leading ? indent : null,
                    leading ? "inherit" : FullChoice("spacing.beforeComma") == "insert" ? "insert" : "remove", listAnchor);
                editor.After(comma.Offset, leading ? "never" : mode, leading ? null : indent,
                    leading ? FullChoice("stackedList.spaceAfterLeadingComma") : "inherit", listAnchor);
            }
        }

        bool Fits(int start, int end, int anchor)
        {
            // Measure the prospective compact form, not padding removed by this very policy.
            var width = editor.GetLineIndent(anchor);
            TSqlParserToken? previous = null;
            foreach (var token in tokens.Where(token => token.Offset >= start && token.Offset < end))
            {
                if (token.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment) return false;
                if (previous is not null)
                    width += measuredGaps.TryGetValue(token.Offset, out var gap) ? gap
                        : NormalizedGap(source.Substring(previous.Offset + previous.Text.Length,
                            token.Offset - previous.Offset - previous.Text.Length));
                width += token.Text.Length;
                previous = token;
            }
            return width <= options.General.MaxLineWidth;
        }
        void MeasureList(TSqlFragment[] items, string stackKey, string indentKey)
        {
            for (var index = 1; index < items.Length; index++)
            {
                var comma = editor.Find(End(items[index - 1]), items[index].StartOffset, token => token.Text == ",");
                if (comma is null) continue;
                if (Choice(stackKey) != "inherit")
                {
                    measuredGaps[comma.Offset] = FullChoice("spacing.beforeComma") == "insert" ? 1 : 0;
                    measuredGaps[items[index].StartOffset] = 1 + InlinePadding(Indent(indentKey));
                }
                else
                {
                    var leading = source.Substring(End(items[index - 1]), comma.Offset - End(items[index - 1]))
                        .IndexOfAny(new[] { '\r', '\n' }) >= 0;
                    var offset = leading ? comma.Offset : items[index].StartOffset;
                    measuredGaps[offset] = GapWidth(offset, stackKey, null, indentKey);
                }
            }
        }
        int GapWidth(int offset, string breakKey, string? spaceKey, string indentKey)
        {
            var left = editor.FindLast(0, offset, _ => true);
            if (left is null) return 0;
            var gap = source.Substring(left.Offset + left.Text.Length, offset - left.Offset - left.Text.Length);
            var mode = Choice(breakKey);
            var space = spaceKey is null ? "inherit" : Choice(spaceKey);
            var indent = Indent(indentKey);
            if (mode == "always") return 1;
            if (mode == "inherit" && gap.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                return indent.Enabled ? 1 : NormalizedGap(gap);
            if (mode == "never" || indent is { Enabled: true, OnNewLineOnly: false })
                return (space == "remove" ? 0 : 1) + InlinePadding(indent);
            return space == "insert" ? 1 : space == "remove" ? 0 : NormalizedGap(gap);
        }
        int InlinePadding(IndentRule indent) => indent is { Enabled: true, OnNewLineOnly: false, Transparent: false }
            ? Math.Max(0, indent.Width(options.Indent.Size)) : 0;
        int NormalizedGap(string gap) => Regex.Replace(gap, @"[ \t]*(?:\r\n|\r|\n)[ \t]*", " ").Length;
        void Boundary(int offset, string breakKey, string indentKey, int anchor) =>
            editor.Before(offset, Choice(breakKey), Indent(indentKey), anchorOffset: anchor);
        string Choice(string key) => FullChoice("merge." + key);
        string FullChoice(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => FullIndent("merge." + key);
        IndentRule FullIndent(string key) => NativeRules.Get(options, key).Indent;
    }

    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
