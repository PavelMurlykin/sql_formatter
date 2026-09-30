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

    public string Apply(CancellationToken cancellationToken)
    {
        return edits.Count == 0 ? parsed.Source : KeywordCasing.Apply(parsed.Source,
            edits.Values.OrderBy(edit => edit.Span.StartOffset).ToArray(), cancellationToken);
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
        var current = parsed.Source.Substring(start, length);
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
        var pendingBreak = edits.Values.Where(edit => edit.Span.EndOffset <= offset
                && edit.NewText.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            .OrderByDescending(edit => edit.Span.StartOffset).FirstOrDefault();
        if (pendingBreak is not null && parsed.Source.Substring(pendingBreak.Span.EndOffset,
                offset - pendingBreak.Span.EndOffset).IndexOfAny(new[] { '\r', '\n' }) < 0)
        {
            var lastBreak = pendingBreak.NewText.LastIndexOfAny(new[] { '\r', '\n' });
            return pendingBreak.NewText.Length - lastBreak - 1;
        }
        var start = Math.Max(parsed.Source.LastIndexOf('\n', Math.Max(0, offset - 1)),
            parsed.Source.LastIndexOf('\r', Math.Max(0, offset - 1))) + 1;
        var end = start;
        var width = 0;
        while (end < parsed.Source.Length && parsed.Source[end] is ' ' or '\t')
        {
            width += parsed.Source[end] == '\t' ? options.Indent.Size : 1;
            end++;
        }
        return width;
    }

    private static bool IsComment(TSqlParserToken token) => token.TokenType is
        TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment;
}
