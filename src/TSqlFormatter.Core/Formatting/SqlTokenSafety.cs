using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Checks exact parser tokens, allowing only casing edits explicitly selected by the profile.</summary>
public static class SqlTokenSafety
{
    public static bool PreservesTokens(SqlParseResult before, SqlParseResult after,
        FormattingOptions options, CancellationToken cancellationToken = default)
    {
        if (!before.ParseSucceeded || !after.ParseSucceeded) return false;
        var casing = KeywordCasing.GetEdits(before, options, cancellationToken)
            .ToDictionary(edit => edit.Span.StartOffset, edit => edit.NewText);
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
