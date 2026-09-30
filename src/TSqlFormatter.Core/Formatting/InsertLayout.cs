using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Independent, opt-in INSERT boundaries, lists, spaces, and SELECT-source layout.</summary>
internal static class InsertLayout
{
    private static readonly Regex Newlines = new(@"[ \t]*(?:\r\n|\n|\r)[ \t]*",
        RegexOptions.CultureInvariant);
    private static readonly Regex Words = new(@"[A-Za-z_@#][A-Za-z_0-9@#$]*|[0-9]+",
        RegexOptions.CultureInvariant);

    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("insert.", StringComparison.Ordinal)))
            return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        foreach (var spec in Specifications(parsed, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = spec.Target;
            var into = editor.Find(spec.StartOffset, target.StartOffset, Is("INTO"));
            if (into is not null)
                editor.Before(into.Offset, Break("into.breakBefore"), Indent("into.keywordIndent"),
                    anchorOffset: spec.StartOffset);
            editor.Before(target.StartOffset, Break("into.breakBeforeTable"), Indent("into.tableIndent"),
                anchorOffset: spec.StartOffset);
            if (spec.Columns.Count > 0)
            {
                var first = spec.Columns[0];
                var last = spec.Columns[spec.Columns.Count - 1];
                var open = editor.Find(target.StartOffset + target.FragmentLength, first.StartOffset,
                    token => token.Text == "(");
                var next = ((TSqlFragment?)spec.OutputIntoClause ?? spec.OutputClause)?.StartOffset
                    ?? spec.InsertSource.StartOffset;
                var close = editor.Find(last.StartOffset + last.FragmentLength, next, token => token.Text == ")");
                if (open is not null && close is not null)
                {
                    editor.Before(open.Offset, Break("columns.breakBeforeOpen"), Indent("columns.braceIndent"),
                        Space("columns.spaceBeforeOpen"), spec.StartOffset);
                    BracedList(spec.Columns.Cast<TSqlFragment>().ToArray(), open, close, "columns", spec.StartOffset,
                        spec.StartOffset);
                }
            }
            if (spec.OutputIntoClause is { } outputInto)
                Output(outputInto, outputInto.SelectColumns.Cast<TSqlFragment>().ToArray(), spec.StartOffset);
            if (spec.OutputClause is { } output)
                Output(output, output.SelectColumns.Cast<TSqlFragment>().ToArray(), spec.StartOffset);
            if (spec.InsertSource is ValuesInsertSource values) Values(values, spec.StartOffset);
            else if (spec.InsertSource is SelectInsertSource { Select: { } query })
                editor.Before(query.StartOffset, Break("source.breakBefore"), anchorOffset: spec.StartOffset);
        }
        var changed = editor.Apply(cancellationToken);
        if (!Valid(changed, parsed)) return source;
        changed = IndentSources(changed, options, parser, dialect, cancellationToken);
        changed = CompactSources(changed, options, parser, dialect, cancellationToken);
        return Valid(changed, parsed) ? changed : source;

        void Output(TSqlFragment clause, TSqlFragment[] items, int anchor)
        {
            if (items.Length == 0) return;
            var keyword = editor.Find(clause.StartOffset, items[0].StartOffset, Is("OUTPUT"));
            if (keyword is null) return;
            editor.Before(keyword.Offset, Break("output.breakBefore"), Indent("output.keywordIndent"),
                anchorOffset: anchor);
            editor.Before(items[0].StartOffset, Break("output.breakAfter"), Indent("output.listIndent"),
                anchorOffset: keyword.Offset);
            List(items, "output.stackList", "output.stackMode", "output.listIndent", keyword.Offset,
                Fits(clause.StartOffset, items[items.Length - 1].StartOffset
                    + items[items.Length - 1].FragmentLength, anchor));
        }

        void Values(ValuesInsertSource values, int anchor)
        {
            var keyword = editor.Find(values.StartOffset, values.RowValues[0].StartOffset, Is("VALUES"));
            if (keyword is null) return;
            editor.Before(keyword.Offset, Break("values.breakBeforeKeyword"), Indent("values.keywordIndent"),
                anchorOffset: anchor);
            editor.Before(values.RowValues[0].StartOffset, Break("values.breakAfterKeyword"),
                Indent("values.braceIndent"), Space("values.spaceAfterKeyword"), anchor);
            foreach (var row in values.RowValues)
            {
                if (row.ColumnValues.Count == 0) continue;
                var open = editor.Find(row.StartOffset, row.ColumnValues[0].StartOffset, token => token.Text == "(");
                var last = row.ColumnValues[row.ColumnValues.Count - 1];
                var close = editor.Find(last.StartOffset + last.FragmentLength,
                    row.StartOffset + row.FragmentLength, token => token.Text == ")");
                if (open is not null && close is not null)
                    BracedList(row.ColumnValues.Cast<TSqlFragment>().ToArray(), open, close, "values", anchor,
                        open.Offset);
            }
            List(values.RowValues.Cast<TSqlFragment>().ToArray(), "values.stackRows", "values.stackRowsMode",
                "values.braceIndent", anchor, Fits(values.StartOffset,
                    values.StartOffset + values.FragmentLength, anchor));
        }

        void BracedList(TSqlFragment[] items, TSqlParserToken open, TSqlParserToken close, string group, int anchor,
            int fitStart)
        {
            editor.Before(items[0].StartOffset, Break(group + ".breakAfterOpen"), Indent(group + ".listIndent"),
                Space(group + ".spaceWithin"), open.Offset);
            List(items, group + ".stackList", group + ".stackMode", group + ".listIndent", open.Offset,
                Fits(fitStart, close.Offset + close.Text.Length, anchor));
            editor.Before(close.Offset, Break(group + ".breakBeforeClose"), Indent(group + ".braceIndent"),
                Space(group + ".spaceWithin"), anchor);
        }

        void List(TSqlFragment[] items, string stackKey, string modeKey, string indentKey, int anchor, bool fits)
        {
            var stack = Break(stackKey);
            var indent = Indent(indentKey);
            if (stack == "inherit" && !indent.Enabled) return;
            var mode = stack == "off" || Break(modeKey) == "auto" && fits ? "never" : "always";
            var leading = NativeRules.Get(options, "stackedList.commaPlacement").Choice == "leading"
                && mode == "always";
            for (var index = 1; index < items.Length; index++)
            {
                var previous = items[index - 1];
                var next = items[index];
                var comma = editor.Find(previous.StartOffset + previous.FragmentLength, next.StartOffset,
                    token => token.Text == ",");
                if (comma is null) continue;
                if (stack == "inherit")
                {
                    editor.After(comma.Offset, "inherit", indent, anchorOffset: anchor);
                    continue;
                }
                editor.Before(comma.Offset, leading ? mode : "never", leading ? indent : null,
                    leading ? "inherit" : SpaceBeforeComma(), anchor);
                editor.After(comma.Offset, leading ? "never" : mode, leading ? null : indent,
                    leading ? NativeRules.Get(options, "stackedList.spaceAfterLeadingComma").Choice : "inherit", anchor);
            }
        }

        bool Fits(int start, int end, int anchor) => LineIndent(source, anchor, options.Indent.Size)
            + Newlines.Replace(source.Substring(start, end - start), " ").Length <= options.General.MaxLineWidth;
        bool Valid(string candidate, SqlParseResult before)
        {
            if (candidate == before.Source) return true;
            var after = parser.Parse(candidate, dialect, cancellationToken);
            return after.ParseSucceeded && SqlSpacing.SameTokens(before, after);
        }
        string SpaceBeforeComma() => NativeRules.Get(options, "spacing.beforeComma").Choice == "insert"
            ? "insert" : "remove";
        string Break(string key) => NativeRules.Get(options, "insert." + key).Choice;
        string Space(string key) => Break(key);
        IndentRule Indent(string key) => NativeRules.Get(options, "insert." + key).Indent;
    }

    private static string IndentSources(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        var indent = NativeRules.Get(options, "insert.source.indent").Indent;
        if (!indent.Enabled) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var edits = new List<TextEdit>();
        foreach (var spec in Specifications(parsed, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (spec.InsertSource is not SelectInsertSource { Select: { } query }) continue;
            var start = LineStart(source, query.StartOffset);
            if (source.Substring(start, query.StartOffset - start).Any(ch => ch is not (' ' or '\t')))
                continue;
            var current = LineIndent(source, query.StartOffset, options.Indent.Size);
            var target = indent.Transparent ? 0 : Math.Max(0, indent.Style == "absolute"
                ? indent.Offset * options.Indent.Size
                : LineIndent(source, spec.StartOffset, options.Indent.Size) + indent.Offset * options.Indent.Size);
            var delta = target - current;
            if (delta == 0) continue;
            foreach (Match line in Regex.Matches(source, @"(?:\A|(?<=\n)|(?<=\r)(?!\n))[ \t]*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (line.Index < start || line.Index >= query.StartOffset + query.FragmentLength) continue;
                if (parsed.Tokens.Any(token => token.Offset < line.Index
                    && token.Offset + token.Text.Length > line.Index)) continue;
                var width = line.Value.Sum(ch => ch == '\t' ? options.Indent.Size : 1);
                edits.Add(new TextEdit(new SqlTextSpan(line.Index, line.Length),
                    new string(' ', Math.Max(0, width + delta))));
            }
        }
        var shifted = KeywordCasing.Apply(source, edits, cancellationToken);
        if (shifted != source)
        {
            var validated = parser.Parse(shifted, dialect, cancellationToken);
            if (!validated.ParseSucceeded || !SqlSpacing.SameTokens(parsed, validated)) return source;
        }
        return IndentInlineSources(shifted);

        string IndentInlineSources(string candidate)
        {
            if (indent.OnNewLineOnly) return candidate;
            var reparsed = parser.Parse(candidate, dialect, cancellationToken);
            var editor = new SqlTokenGapEditor(reparsed, options);
            foreach (var spec in Specifications(reparsed, cancellationToken))
                if (spec.InsertSource is SelectInsertSource { Select: { } query }
                    && candidate.Substring(LineStart(candidate, query.StartOffset),
                        query.StartOffset - LineStart(candidate, query.StartOffset)).Any(ch => ch is not (' ' or '\t')))
                    editor.Before(query.StartOffset, "inherit", indent, anchorOffset: spec.StartOffset);
            return editor.Apply(cancellationToken);
        }
    }

    private static string CompactSources(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        var any = NativeRules.Get(options, "insert.source.singleLine.any").Boolean;
        var fits = NativeRules.Get(options, "insert.source.singleLine.whenFitsMargin").Boolean;
        var words = NativeRules.Get(options, "insert.source.singleLine.maxWords").Threshold;
        var characters = NativeRules.Get(options, "insert.source.singleLine.maxCharacters").Threshold;
        if (!any && !fits && !words.Enabled && !characters.Enabled) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var edits = new List<TextEdit>();
        foreach (var spec in Specifications(parsed, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (spec.InsertSource is not SelectInsertSource { Select: { } query }) continue;
            var start = query.StartOffset;
            var end = start + query.FragmentLength;
            if (parsed.Tokens.Any(token => token.Offset >= start && token.Offset < end
                && token.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)
                && (token.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment
                    || token.Text.IndexOfAny(new[] { '\r', '\n' }) >= 0))) continue;
            var flat = Newlines.Replace(source.Substring(start, query.FragmentLength), " ");
            var lineEnd = source.IndexOfAny(new[] { '\r', '\n' }, end);
            if (lineEnd < 0) lineEnd = source.Length;
            var fitsMargin = start - LineStart(source, start) + flat.Length + lineEnd - end
                <= options.General.MaxLineWidth;
            if (any || fits && fitsMargin || words.Enabled && Words.Matches(flat).Count < words.Value
                || characters.Enabled && flat.Length < characters.Value)
                edits.Add(new TextEdit(new SqlTextSpan(start, query.FragmentLength), flat));
        }
        var changed = KeywordCasing.Apply(source, edits, cancellationToken);
        if (changed == source) return source;
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;
    }

    private static IEnumerable<InsertSpecification> Specifications(SqlParseResult parsed,
        CancellationToken cancellationToken) => parsed.Root is null ? Array.Empty<InsertSpecification>()
        : new SqlFragmentWalker().Walk(parsed.Root, cancellationToken).OfType<InsertStatement>()
            .Select(statement => statement.InsertSpecification)
            .Where(spec => spec is { Target: NamedTableReference or VariableTableReference }
                && (spec.InsertSource is SelectInsertSource
                    || spec.InsertSource is ValuesInsertSource { IsDefaultValues: false, RowValues.Count: > 0 }));

    private static int LineStart(string source, int offset) =>
        source.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, offset - 1)) + 1;
    private static int LineIndent(string source, int offset, int size)
    {
        var width = 0;
        for (var index = LineStart(source, offset); index < source.Length && source[index] is ' ' or '\t'; index++)
            width += source[index] == '\t' ? size : 1;
        return width;
    }
    private static Func<TSqlParserToken, bool> Is(string word) => token =>
        token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
