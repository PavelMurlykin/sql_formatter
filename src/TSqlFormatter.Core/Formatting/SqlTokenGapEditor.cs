using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Edits only comment-free whitespace between adjacent parser tokens.</summary>
internal sealed class SqlTokenGapEditor
{
    private readonly SqlParseResult parsed;
    private readonly FormattingOptions options;
    private readonly TSqlParserToken[] tokens;
    private readonly Dictionary<int, int> indices;
    private readonly Dictionary<int, TextEdit> edits = new();

    public SqlTokenGapEditor(SqlParseResult parsed, FormattingOptions options)
    {
        this.parsed = parsed;
        this.options = options;
        tokens = parsed.Tokens.Where(token => token.TokenType is not
            (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile) && token.Text.Length > 0).ToArray();
        indices = tokens.Select((token, index) => (token.Offset, index))
            .ToDictionary(pair => pair.Offset, pair => pair.index);
    }

    public TSqlParserToken? Find(int start, int end, Func<TSqlParserToken, bool> match) =>
        tokens.FirstOrDefault(token => token.Offset >= start && token.Offset < end && match(token));

    public TSqlParserToken? FindLast(int start, int end, Func<TSqlParserToken, bool> match) =>
        tokens.LastOrDefault(token => token.Offset >= start && token.Offset < end && match(token));

    public void Before(int offset, string breakMode, IndentRule? indent = null,
        string spaceMode = "inherit", int? anchorOffset = null)
    {
        if (indices.TryGetValue(offset, out var index) && index > 0)
            Set(index - 1, index, breakMode, indent, spaceMode, anchorOffset);
    }

    public void After(int offset, string breakMode, IndentRule? indent = null,
        string spaceMode = "inherit", int? anchorOffset = null)
    {
        if (indices.TryGetValue(offset, out var index) && index + 1 < tokens.Length)
            Set(index, index + 1, breakMode, indent, spaceMode, anchorOffset);
    }

    /// <summary>Retain an already-indented first line before shifting a whole fragment.</summary>
    public void BeforeFragment(int offset, string breakMode, int anchorOffset)
    {
        if (breakMode == "always" && indices.TryGetValue(offset, out var index) && index > 0)
        {
            var previous = tokens[index - 1];
            var start = previous.Offset + previous.Text.Length;
            if (parsed.Source.Substring(start, offset - start).IndexOfAny(new[] { '\r', '\n' }) >= 0)
                breakMode = "inherit";
        }
        Before(offset, breakMode, anchorOffset: anchorOffset);
    }

    public string Apply(CancellationToken cancellationToken)
    {
        return edits.Count == 0 ? parsed.Source : KeywordCasing.Apply(parsed.Source,
            edits.Values.OrderBy(edit => edit.Span.StartOffset).ToArray(), cancellationToken);
    }

    public void BlankBefore(int offset)
    {
        if (!indices.TryGetValue(offset, out var index) || index == 0) return;
        var left = tokens[index - 1];
        var right = tokens[index];
        if (IsComment(left) || IsComment(right)) return;
        var start = left.Offset + left.Text.Length;
        var length = right.Offset - start;
        var current = edits.TryGetValue(start, out var edit) ? edit.NewText : parsed.Source.Substring(start, length);
        if (current.Any(ch => !char.IsWhiteSpace(ch))) return;
        var newline = options.General.LineEnding switch
        { DocLineEnding.CrLf => "\r\n", DocLineEnding.Cr => "\r", _ => "\n" };
        if (Regex.Matches(current, @"\r\n|\r|\n").Count >= 2) return;
        edits[start] = new TextEdit(new SqlTextSpan(start, length), newline + newline + new string(' ', LineIndent(offset)));
    }

    public int GetLineIndent(int offset) => LineIndent(offset);

    public void IndentAtBoundary(int offset, IndentRule indent, int anchorOffset)
    {
        if (!indent.Enabled || !indices.TryGetValue(offset, out var index)) return;
        if (index > 0) { Before(offset, "inherit", indent, anchorOffset: anchorOffset); return; }
        var prefix = parsed.Source.Substring(0, offset);
        if (prefix.Any(ch => !char.IsWhiteSpace(ch))) return;
        var lastBreak = prefix.LastIndexOfAny(new[] { '\r', '\n' });
        var start = lastBreak + 1;
        var replacement = new string(' ', IndentWidth(LineIndent(anchorOffset), indent));
        if (prefix.Substring(start) != replacement)
            edits[start] = new TextEdit(new SqlTextSpan(start, offset - start), replacement);
    }

    private void Set(int leftIndex, int rightIndex, string breakMode, IndentRule? indent,
        string spaceMode, int? anchorOffset)
    {
        if (breakMode == "inherit" && indent?.Enabled != true && spaceMode == "inherit") return;
        var left = tokens[leftIndex];
        var right = tokens[rightIndex];
        if (IsComment(left) || IsComment(right)) return;
        var start = left.Offset + left.Text.Length;
        var length = right.Offset - start;
        if (length < 0) return;
        var current = edits.TryGetValue(start, out var pending) ? pending.NewText : parsed.Source.Substring(start, length);
        if (current.Any(ch => !char.IsWhiteSpace(ch))) return;
        var existingBreak = Regex.Match(current, @"\r\n|\r|\n");
        var newline = options.General.LineEnding switch
        {
            DocLineEnding.CrLf => "\r\n", DocLineEnding.Cr => "\r", _ => "\n"
        };
        string replacement;
        if (breakMode == "always")
        {
            var baseIndent = LineIndent(anchorOffset ?? left.Offset);
            replacement = newline + new string(' ', IndentWidth(baseIndent, indent));
        }
        else if (breakMode == "never")
        {
            replacement = (spaceMode == "remove" ? "" : " ") + (indent is { Enabled: true, OnNewLineOnly: false }
                ? new string(' ', indent.Transparent ? 0 : Math.Max(0, indent.Offset * options.Indent.Size)) : "");
        }
        else if (existingBreak.Success)
        {
            if (indent?.Enabled != true) return;
            replacement = current.Substring(0, existingBreak.Index + existingBreak.Length)
                + new string(' ', IndentWidth(LineIndent(anchorOffset ?? left.Offset), indent));
        }
        else if (indent is { Enabled: true, OnNewLineOnly: false })
        {
            replacement = (spaceMode == "remove" ? "" : " ")
                + new string(' ', indent.Transparent ? 0 : Math.Max(0, indent.Offset * options.Indent.Size));
        }
        else if (spaceMode != "inherit") replacement = spaceMode == "insert" ? " " : "";
        else return;
        if (current != replacement) edits[start] = new TextEdit(new SqlTextSpan(start, length), replacement);
    }

    private int IndentWidth(int baseIndent, IndentRule? indent)
    {
        if (indent?.Enabled != true) return baseIndent;
        if (indent.Transparent) return 0;
        var offset = indent.Offset * options.Indent.Size;
        return Math.Max(0, indent.Style == "absolute" ? offset : baseIndent + offset);
    }

    private int LineIndent(int offset)
    {
        // Read the pending line backwards. Removed breaks must not hide a newly inline anchor.
        var chunks = new Stack<string>();
        var end = offset;
        foreach (var edit in edits.Values.Where(edit => edit.Span.EndOffset <= offset)
                     .OrderByDescending(edit => edit.Span.StartOffset))
        {
            if (Push(parsed.Source, edit.Span.EndOffset, end - edit.Span.EndOffset)
                || Push(edit.NewText, 0, edit.NewText.Length)) return Width();
            end = edit.Span.StartOffset;
        }
        Push(parsed.Source, 0, end);
        return Width();

        bool Push(string text, int start, int length)
        {
            var lastBreak = length == 0 ? -1 : text.LastIndexOfAny(new[] { '\r', '\n' }, start + length - 1, length);
            var chunkStart = lastBreak >= 0 ? lastBreak + 1 : start;
            chunks.Push(text.Substring(chunkStart, start + length - chunkStart));
            return lastBreak >= 0;
        }
        int Width()
        {
            var width = 0;
            foreach (var chunk in chunks)
                foreach (var ch in chunk)
                {
                    if (ch is not (' ' or '\t')) return width;
                    width += ch == '\t' ? options.Indent.Size : 1;
                }
            return width;
        }
    }

    private static bool IsComment(TSqlParserToken token) => token.TokenType is
        TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment;
}
