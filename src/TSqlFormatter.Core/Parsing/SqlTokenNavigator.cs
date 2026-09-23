using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Parsing;

/// <summary>Provides source-preserving access to a parse result's tokens and AST fragments.</summary>
public sealed class SqlTokenNavigator
{
    private readonly SqlParseResult _result;

    public SqlTokenNavigator(SqlParseResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public TSqlParserToken GetToken(int index)
    {
        ValidateTokenIndex(index);
        return _result.Tokens[index];
    }

    /// <summary>Finds the nearest non-trivia token strictly before the specified token.</summary>
    public TSqlParserToken? GetPreviousMeaningfulToken(int index)
    {
        ValidateTokenIndex(index);

        for (var previous = index - 1; previous >= 0; previous--)
        {
            var token = _result.Tokens[previous];
            if (IsMeaningful(token))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>Finds the nearest non-trivia token strictly after the specified token.</summary>
    public TSqlParserToken? GetNextMeaningfulToken(int index)
    {
        ValidateTokenIndex(index);

        for (var next = index + 1; next < _result.Tokens.Count; next++)
        {
            var token = _result.Tokens[next];
            if (IsMeaningful(token))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>Returns the inclusive token range of a fragment, including trivia within it.</summary>
    public IReadOnlyList<TSqlParserToken> GetFragmentTokens(TSqlFragment fragment)
    {
        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        var first = fragment.FirstTokenIndex;
        var last = fragment.LastTokenIndex;
        if (first < 0 || last < first || last >= _result.Tokens.Count)
        {
            throw new ArgumentException("The fragment has no valid token range in this parse result.", nameof(fragment));
        }

        var tokens = new TSqlParserToken[last - first + 1];
        for (var index = 0; index < tokens.Length; index++)
        {
            tokens[index] = _result.Tokens[first + index];
        }

        return Array.AsReadOnly(tokens);
    }

    /// <summary>Returns the fragment's character range in the original SQL source.</summary>
    public SqlTextSpan GetTextSpan(TSqlFragment fragment)
    {
        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        var start = fragment.StartOffset;
        var length = fragment.FragmentLength;
        if (start < 0 || length < 0 || start > _result.Source.Length || length > _result.Source.Length - start)
        {
            throw new ArgumentException("The fragment has no valid text range in this parse result.", nameof(fragment));
        }

        return new SqlTextSpan(start, length);
    }

    private void ValidateTokenIndex(int index)
    {
        if (index < 0 || index >= _result.Tokens.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private static bool IsMeaningful(TSqlParserToken token)
    {
        return token.TokenType != TSqlTokenType.WhiteSpace
            && token.TokenType != TSqlTokenType.SingleLineComment
            && token.TokenType != TSqlTokenType.MultilineComment
            && token.TokenType != TSqlTokenType.EndOfFile;
    }
}
