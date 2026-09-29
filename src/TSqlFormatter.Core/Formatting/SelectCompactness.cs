using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Opt-in one-line layout for simple, comment-free top-level SELECT statements.</summary>
internal static class SelectCompactness
{
    private static readonly Regex Newlines = new(@"[ \t]*(?:\r\n|\n|\r)[ \t]*",
        RegexOptions.CultureInvariant);
    private static readonly Regex Words = new(@"[A-Za-z_@#][A-Za-z_0-9@#$]*|[0-9]+",
        RegexOptions.CultureInvariant);

    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        var words = NativeRules.Get(options, "select.singleLine.maxWords").Threshold;
        var characters = NativeRules.Get(options, "select.singleLine.maxCharacters").Threshold;
        var fitsMargin = NativeRules.Get(options, "select.singleLine.whenFitsMargin").Boolean;
        if (!words.Enabled && !characters.Enabled && !fitsMargin) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is not TSqlScript script) return source;
        var edits = new List<TextEdit>();
        foreach (var statement in script.Batches.SelectMany(batch => batch.Statements))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement is not SelectStatement select
                || select.QueryExpression is not QuerySpecification query
                || query.SelectElements.Count == 0 || select.WithCtesAndXmlNamespaces is not null
                || statement.StartOffset < 0 || statement.FragmentLength <= 0
                || statement.StartOffset + statement.FragmentLength > source.Length) continue;
            var start = statement.StartOffset;
            var end = start + statement.FragmentLength;
            var tokens = parsed.Tokens.Where(token => token.Offset >= start && token.Offset < end
                && token.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).ToArray();
            if (tokens.Any(token => token.TokenType is TSqlTokenType.SingleLineComment
                    or TSqlTokenType.MultilineComment || token.Text.IndexOfAny(new[] { '\r', '\n' }) >= 0))
                continue;
            var original = source.Substring(start, statement.FragmentLength);
            var candidate = Newlines.Replace(original, " ");
            if (candidate == original || candidate.Length > options.General.MaxLineWidth) continue;
            var qualifies = fitsMargin || characters.Enabled && candidate.Length <= characters.Value
                || words.Enabled && Words.Matches(candidate).Count <= words.Value;
            if (qualifies)
                edits.Add(new TextEdit(new SqlTextSpan(start, statement.FragmentLength), candidate));
        }
        if (edits.Count == 0) return source;
        var changed = KeywordCasing.Apply(source, edits, cancellationToken);
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;
    }
}
