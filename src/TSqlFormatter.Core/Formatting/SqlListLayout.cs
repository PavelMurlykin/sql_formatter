using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Shared opt-in comma-list layout. Auto measures a stable normalized compact token form.</summary>
internal sealed class SqlListLayout
{
    private readonly SqlParseResult parsed;
    private readonly FormattingOptions options;
    private readonly SqlTokenGapEditor editor;
    public SqlListLayout(SqlParseResult parsed, FormattingOptions options, SqlTokenGapEditor editor)
    {
        this.parsed = parsed;
        this.options = options;
        this.editor = editor;
    }

    public bool Apply(TSqlFragment[] items, string prefix, int anchor, int? start = null, int? end = null)
    {
        if (items.Length == 0) return false;
        var stack = Choice(prefix + ".stackList");
        var indent = NativeRules.Get(options, prefix + ".listIndent").Indent;
        if (stack == "inherit" && !indent.Enabled) return false;
        var compact = stack == "off" || stack == "on" && Choice(prefix + ".stackMode") == "auto"
            && Fits(start ?? items[0].StartOffset, end ?? End(items[items.Length - 1]), anchor, indent, prefix);
        var mode = compact ? "never" : "always";
        var leading = !compact && Choice("stackedList.commaPlacement") == "leading";
        for (var index = 1; index < items.Length; index++)
        {
            var previousEnd = End(items[index - 1]);
            var comma = editor.Find(previousEnd, items[index].StartOffset, token => token.Text == ",");
            if (comma is null) continue;
            if (stack == "inherit")
            {
                var isLeading = parsed.Source.Substring(previousEnd, comma.Offset - previousEnd)
                    .IndexOfAny(new[] { '\r', '\n' }) >= 0;
                if (isLeading) editor.Before(comma.Offset, "inherit", indent, anchorOffset: anchor);
                else editor.After(comma.Offset, "inherit", indent, anchorOffset: anchor);
                continue;
            }
            editor.Before(comma.Offset, leading ? mode : "never", leading ? indent : null,
                leading ? "inherit" : Choice("spacing.beforeComma") == "insert" ? "insert" : "remove", anchor);
            editor.After(comma.Offset, leading ? "never" : mode, leading ? null : indent,
                leading ? Choice("stackedList.spaceAfterLeadingComma") : "inherit", anchor);
        }
        return compact;
    }

    private bool Fits(int start, int end, int anchor, IndentRule indent, string prefix)
    {
        var width = editor.GetLineIndent(anchor);
        TSqlParserToken? previous = null;
        foreach (var token in parsed.Tokens.Where(t => t.Offset >= start && t.Offset < end
                     && t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)))
        {
            if (token.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment) return false;
            if (previous is not null)
            {
                var gap = parsed.Source.Substring(previous.Offset + previous.Text.Length,
                    token.Offset - previous.Offset - previous.Text.Length);
                var brace = previous.Offset == start && previous.Text == "(" ? "breakAfterOpen"
                    : token.Offset + token.Text.Length == end && token.Text == ")" ? "breakBeforeClose" : null;
                width += brace is not null && options.Rules.Catalog.TryGet(prefix + "." + brace, out _)
                    ? Choice(prefix + "." + brace) == "always" ? 1
                        : Choice(prefix + ".spaceWithin") == "insert" ? 1
                        : Choice(prefix + ".spaceWithin") == "remove" ? 0 : gap.Length > 0 ? 1 : 0
                    : token.Text == "," ? Choice("spacing.beforeComma") == "insert" ? 1 : 0
                    : previous.Text == "," ? 1 + (indent is { Enabled: true, OnNewLineOnly: false, Transparent: false }
                        ? Math.Max(0, indent.Offset * options.Indent.Size) : 0)
                    : gap.Length > 0 ? 1 : 0;
            }
            width += token.Text.Length;
            previous = token;
        }
        return width <= options.General.MaxLineWidth;
    }
    private string Choice(string key) => NativeRules.Get(options, key).Choice;
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
}
