using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Opt-in leading commas in vertical lists and blank lines around GO batches.</summary>
internal static class SqlStackedListsAndBatches
{
    private static readonly Regex VerticalGap = new(@"^[ \t]*(\r\n|\n|\r)([ \t]*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex GoLine = new(@"^[ \t]*GO[ \t]*(?:--[^\r\n]*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        var placement = Choice(options, "stackedList.commaPlacement");
        var space = Choice(options, "stackedList.spaceAfterLeadingComma");
        var blank = Boolean(options, "misc.packageDelimiterBlankLine");
        if (placement == "inherit" && space == "inherit" && !blank) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded) return source;
        var changed = ApplyCommas(parsed, placement, space, cancellationToken);
        if (blank) changed = ApplyGoBlankLines(changed,
            Choice(options, "misc.packageDelimiterBlankLineMode"), cancellationToken);
        if (changed == source) return source;
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;
    }

    private static string ApplyCommas(SqlParseResult parsed, string placement, string space,
        CancellationToken cancellationToken)
    {
        if (placement == "inherit" && space == "inherit") return parsed.Source;
        var tokens = parsed.Tokens.Where(token => token.TokenType is not
            (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile) && token.Text.Length > 0).ToArray();
        var edits = new List<TextEdit>();
        for (var index = 1; index + 1 < tokens.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var comma = tokens[index];
            if (comma.Text != "," || Comment(tokens[index - 1]) || Comment(tokens[index + 1])) continue;
            var left = tokens[index - 1];
            var right = tokens[index + 1];
            var leftEnd = left.Offset + left.Text.Length;
            var commaEnd = comma.Offset + comma.Text.Length;
            if (leftEnd > comma.Offset || commaEnd > right.Offset) continue;
            var before = parsed.Source.Substring(leftEnd, comma.Offset - leftEnd);
            var after = parsed.Source.Substring(commaEnd, right.Offset - commaEnd);
            var leading = VerticalGap.Match(before);
            var trailing = VerticalGap.Match(after);
            if (trailing.Success && before.All(ch => ch is ' ' or '\t') && placement == "leading")
            {
                var replacement = trailing.Groups[1].Value + trailing.Groups[2].Value + ","
                    + (space == "remove" ? "" : " ");
                edits.Add(new TextEdit(new SqlTextSpan(leftEnd, right.Offset - leftEnd), replacement));
            }
            else if (leading.Success && after.All(ch => ch is ' ' or '\t'))
            {
                if (placement == "trailing")
                {
                    var replacement = "," + leading.Groups[1].Value + leading.Groups[2].Value;
                    edits.Add(new TextEdit(new SqlTextSpan(leftEnd, right.Offset - leftEnd), replacement));
                }
                else if (space != "inherit")
                {
                    var replacement = space == "insert" ? " " : "";
                    if (after != replacement)
                        edits.Add(new TextEdit(new SqlTextSpan(commaEnd, after.Length), replacement));
                }
            }
        }
        return edits.Count == 0 ? parsed.Source : KeywordCasing.Apply(parsed.Source, edits, cancellationToken);
    }

    private static string ApplyGoBlankLines(string source, string mode,
        CancellationToken cancellationToken)
    {
        var segments = Regex.Split(source, "(\r\n|\n|\r)");
        var output = new StringBuilder(source.Length + 32);
        var count = (segments.Length + 1) / 2;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = segments[index * 2];
            var separator = index * 2 + 1 < segments.Length ? segments[index * 2 + 1] : "";
            var isGo = GoLine.IsMatch(line);
            if (isGo && mode is "before" or "both" && index > 0
                && segments[(index - 1) * 2].Trim().Length > 0)
                output.Append(segments[(index - 1) * 2 + 1]);
            output.Append(line).Append(separator);
            if (isGo && mode is "after" or "both" && index + 1 < count
                && separator.Length > 0 && segments[(index + 1) * 2].Trim().Length > 0)
                output.Append(separator);
        }
        return output.ToString();
    }

    private static bool Comment(TSqlParserToken token) => token.TokenType is
        TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment;

    private static string Choice(FormattingOptions options, string key) =>
        options.Rules.Catalog.TryGet(key, out _) ? options.Rules.Get(key).Choice
            : RuleCatalog.Default.Definitions[key].DefaultValue.Choice;

    private static bool Boolean(FormattingOptions options, string key) =>
        options.Rules.Catalog.TryGet(key, out _) ? options.Rules.Get(key).Boolean
            : RuleCatalog.Default.Definitions[key].DefaultValue.Boolean;
}
