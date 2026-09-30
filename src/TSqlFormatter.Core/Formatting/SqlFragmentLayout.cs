using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

internal sealed class FragmentIndent
{
    public FragmentIndent(TSqlFragment fragment, int anchor, IndentRule rule, int? endOffset = null)
    { Fragment = fragment; Anchor = anchor; Rule = rule; EndOffset = endOffset; }
    private int? EndOffset { get; }
    public TSqlFragment Fragment { get; }
    public int StartOffset => Fragment is StatementList { Statements.Count: > 0 } list ? list.Statements[0].StartOffset : Fragment.StartOffset;
    public int FragmentLength => EndOffset is { } end ? end - StartOffset : Fragment is StatementList { Statements.Count: > 0 } list
        ? list.Statements[list.Statements.Count - 1].StartOffset + list.Statements[list.Statements.Count - 1].FragmentLength - StartOffset
        : Fragment.FragmentLength;
    public int Anchor { get; }
    public IndentRule Rule { get; }
}

/// <summary>Token-preserving whole-fragment indentation and compactness for embedded queries/bodies.</summary>
internal static class SqlFragmentLayout
{
    public static string IndentTreeSafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken,
        Func<TSqlFragment, IEnumerable<FragmentIndent>> select)
    {
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var initial = select(parsed.Root).Where(i => i.Rule.Enabled).ToArray();
        var levels = initial.Length == 0 ? 0 : initial.Max(i => Depth(i, initial)) + 1;
        for (var level = 0; level < levels; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source = IndentSafe(source, options, parser, dialect, cancellationToken, root =>
            {
                var items = select(root).Where(i => i.Rule.Enabled).ToArray();
                return items.Where(i => Depth(i, items) == level);
            });
        }
        return source;
        static int Depth(FragmentIndent item, FragmentIndent[] items) => items.Count(parent =>
            parent.StartOffset <= item.StartOffset
            && parent.StartOffset + parent.FragmentLength >= item.StartOffset + item.FragmentLength
            && parent.FragmentLength > item.FragmentLength);
    }

    public static string IndentSafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken,
        Func<TSqlFragment, IEnumerable<FragmentIndent>> select)
    {
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var edits = new List<TextEdit>();
        var lastEnd = -1;
        // Parents first; descendants are left for a subsequent caller to avoid overlapping edits.
        foreach (var item in select(parsed.Root).OrderBy(i => i.StartOffset).ThenByDescending(i => i.FragmentLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rule = item.Rule;
            if (!rule.Enabled || item.StartOffset < 0 || item.StartOffset < lastEnd) continue;
            var lineStart = LineStart(source, item.StartOffset);
            if (source.Substring(lineStart, item.StartOffset - lineStart).Any(ch => ch is not (' ' or '\t')))
            {
                editor.Before(item.StartOffset, "inherit", rule, anchorOffset: item.Anchor);
                continue;
            }
            var target = rule.Transparent ? 0 : Math.Max(0, (rule.Style == "absolute" ? 0 : editor.GetLineIndent(item.Anchor))
                + rule.Offset * options.Indent.Size);
            var delta = target - editor.GetLineIndent(item.StartOffset);
            if (delta == 0) continue;
            lastEnd = item.StartOffset + item.FragmentLength;
            foreach (Match line in Regex.Matches(source, @"(?:\A|(?<=\n)|(?<=\r)(?!\n))[ \t]*"))
            {
                if (line.Index < lineStart || line.Index >= lastEnd) continue;
                if (parsed.Tokens.Any(t => t.Offset < line.Index && t.Offset + t.Text.Length > line.Index)) continue;
                var width = line.Value.Sum(ch => ch == '\t' ? options.Indent.Size : 1);
                edits.Add(new TextEdit(new SqlTextSpan(line.Index, line.Length), new string(' ', Math.Max(0, width + delta))));
            }
        }
        // Gap edits apply only to inline fragments and cannot overlap whole-line indentation edits.
        var shifted = KeywordCasing.Apply(source, edits, cancellationToken);
        if (edits.Count == 0) shifted = editor.Apply(cancellationToken);
        else
        {
            var reparsed = parser.Parse(shifted, dialect, cancellationToken);
            if (!reparsed.ParseSucceeded || reparsed.Root is null) return source;
            var inline = new SqlTokenGapEditor(reparsed, options);
            foreach (var item in select(reparsed.Root))
                if (item.Rule.Enabled && item.StartOffset >= 0 && shifted.Substring(LineStart(shifted, item.StartOffset),
                        item.StartOffset - LineStart(shifted, item.StartOffset)).Any(ch => ch is not (' ' or '\t')))
                    inline.Before(item.StartOffset, "inherit", item.Rule, anchorOffset: item.Anchor);
            shifted = inline.Apply(cancellationToken);
        }
        return Valid(shifted, parsed, parser, dialect, cancellationToken) ? shifted : source;
    }

    public static string CompactSafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken, string prefix,
        Func<TSqlFragment, IEnumerable<TSqlFragment>> select)
    {
        var any = NativeRules.Get(options, prefix + ".any").Boolean;
        var fits = NativeRules.Get(options, prefix + ".whenFitsMargin").Boolean;
        var words = NativeRules.Get(options, prefix + ".maxWords").Threshold;
        var characters = NativeRules.Get(options, prefix + ".maxCharacters").Threshold;
        if (!any && !fits && !words.Enabled && !characters.Enabled) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var edits = new List<TextEdit>();
        var end = -1;
        foreach (var fragment in select(parsed.Root).OrderBy(f => f.StartOffset).ThenByDescending(f => f.FragmentLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fragment.StartOffset < end) continue;
            var finish = fragment.StartOffset + fragment.FragmentLength;
            if (parsed.Tokens.Any(t => t.Offset >= fragment.StartOffset && t.Offset < finish
                    && t.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)) continue;
            var flat = Regex.Replace(source.Substring(fragment.StartOffset, fragment.FragmentLength), @"[ \t]*(?:\r\n|\r|\n)[ \t]*", " ");
            var suffix = source.IndexOfAny(new[] { '\r', '\n' }, finish);
            if (suffix < 0) suffix = source.Length;
            var fitsMargin = fragment.StartOffset - LineStart(source, fragment.StartOffset) + flat.Length + suffix - finish
                <= options.General.MaxLineWidth;
            if (any || fits && fitsMargin || words.Enabled && Regex.Matches(flat, @"[A-Za-z_@#][A-Za-z_0-9@#$]*|[0-9]+").Count < words.Value
                || characters.Enabled && flat.Length < characters.Value)
            {
                edits.Add(new TextEdit(new SqlTextSpan(fragment.StartOffset, fragment.FragmentLength), flat));
                end = finish;
            }
        }
        var changed = KeywordCasing.Apply(source, edits, cancellationToken);
        return Valid(changed, parsed, parser, dialect, cancellationToken) ? changed : source;
    }

    private static int LineStart(string source, int offset) => source.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, offset - 1)) + 1;
    private static bool Valid(string candidate, SqlParseResult before, ISqlParser parser, SqlDialectVersion dialect,
        CancellationToken cancellationToken)
    {
        if (candidate == before.Source) return true;
        var after = parser.Parse(candidate, dialect, cancellationToken);
        return after.ParseSucceeded && SqlSpacing.SameTokens(before, after);
    }
}
