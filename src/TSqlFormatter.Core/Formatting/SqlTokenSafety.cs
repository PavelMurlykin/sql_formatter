using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Checks exact parser tokens against the profile's explicitly permitted casing and syntax edits.</summary>
public static class SqlTokenSafety
{
    public static bool PreservesTokens(SqlParseResult before, SqlParseResult after,
        FormattingOptions options, CancellationToken cancellationToken = default) =>
        PreservesTokens(before, after, options, cancellationToken, null);

    public static bool PreservesTokens(SqlParseResult before, SqlParseResult after,
        FormattingOptions options, CancellationToken cancellationToken, SqlTextSpan? allowedSpan)
    {
        if (!before.ParseSucceeded || !after.ParseSucceeded) return false;
        var protectedSpans = SqlSyntaxPolicy.ProtectedSpans(before, options);
        var casing = KeywordCasing.GetEdits(before, options, cancellationToken)
            .Where(edit => allowedSpan is null || edit.Span.StartOffset >= allowedSpan.Value.StartOffset && edit.Span.EndOffset <= allowedSpan.Value.EndOffset)
            .Where(edit => !protectedSpans.Any(span => edit.Span.StartOffset >= span.StartOffset && edit.Span.StartOffset < span.EndOffset))
            .ToDictionary(edit => edit.Span.StartOffset, edit => edit.NewText);
        if (SqlSyntaxPolicy.Enabled(options))
        {
            var edits = before.Tokens.Where(t => casing.ContainsKey(t.Offset)).Select(t =>
                new TextEdit(new SqlTextSpan(t.Offset, t.Text.Length), casing[t.Offset])).ToArray();
            string expected = KeywordCasing.Apply(before.Source, edits, cancellationToken);
            var parser = new ScriptDomSqlParser();
            int syntaxSourceLength = expected.Length;
            expected = SqlSyntaxPolicy.ApplySafe(expected, options, parser, before.RequestedDialect, cancellationToken, allowedSpan);
            var permitted = parser.Parse(expected, before.RequestedDialect, cancellationToken);
            if (!permitted.ParseSucceeded) return false;
            // Removing quotes makes identifier/alias casing eligible even when quoted-name
            // casing is disabled. Match that permitted composition on subsequent passes.
            SqlTextSpan? mappedSpan = allowedSpan is { } span
                ? new SqlTextSpan(span.StartOffset, span.Length + expected.Length - syntaxSourceLength) : null;
            var protectedAfterSyntax = SqlSyntaxPolicy.ProtectedSpans(permitted, options);
            var afterSyntaxCasing = KeywordCasing.GetEdits(permitted, options, cancellationToken)
                .Where(edit => mappedSpan is null || edit.Span.StartOffset >= mappedSpan.Value.StartOffset && edit.Span.EndOffset <= mappedSpan.Value.EndOffset)
                .Where(edit => !protectedAfterSyntax.Any(p => edit.Span.StartOffset >= p.StartOffset && edit.Span.StartOffset < p.EndOffset))
                .ToArray();
            if (afterSyntaxCasing.Length > 0)
                permitted = parser.Parse(KeywordCasing.Apply(expected, afterSyntaxCasing, cancellationToken), before.RequestedDialect, cancellationToken);
            return permitted.ParseSucceeded && permitted.Tokens.Where(Meaningful).Select(t => (t.TokenType, t.Text))
                .SequenceEqual(after.Tokens.Where(Meaningful).Select(t => (t.TokenType, t.Text)));
        }
        var left = before.Tokens.Where(Meaningful).ToArray();
        var right = after.Tokens.Where(Meaningful).ToArray();
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expected = casing.TryGetValue(left[index].Offset, out var replacement) ? replacement : left[index].Text;
            if (left[index].TokenType != right[index].TokenType || expected != right[index].Text) return false;
        }
        return true;
    }

    private static bool Meaningful(TSqlParserToken token) => token.TokenType is not
        (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile);
}
