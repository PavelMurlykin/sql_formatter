using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Parsing;

/// <summary>Classifies source comments as leading, trailing, or standalone trivia.</summary>
public sealed class SqlTriviaScanner
{
    public IReadOnlyList<SqlCommentTrivia> Scan(
        SqlParseResult result,
        CancellationToken cancellationToken = default)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));

        var tokens = result.Tokens;
        var classified = new SqlCommentTrivia?[tokens.Count];
        var previousMeaningful = new int[tokens.Count];
        var lastMeaningful = -1;
        for (var index = 0; index < tokens.Count; index++)
        {
            previousMeaningful[index] = lastMeaningful;
            if (IsMeaningful(tokens[index])) lastMeaningful = index;
        }

        var nextNonWhitespace = -1;
        for (var index = tokens.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = tokens[index];
            if (IsComment(token))
            {
                var span = new SqlTextSpan(token.Offset, token.Text.Length);
                var previous = previousMeaningful[index];
                SqlTriviaPlacement placement;
                int? anchor;
                if (previous >= 0
                    && LineAtEnd(result, tokens[previous]) == result.LineMap.GetLinePosition(span.StartOffset).Line)
                {
                    placement = SqlTriviaPlacement.Trailing;
                    anchor = previous;
                }
                else if (nextNonWhitespace >= 0
                    && IsAdjacent(result, token, tokens[nextNonWhitespace])
                    && (IsMeaningful(tokens[nextNonWhitespace])
                        || classified[nextNonWhitespace]?.Placement == SqlTriviaPlacement.Leading))
                {
                    placement = SqlTriviaPlacement.Leading;
                    anchor = IsMeaningful(tokens[nextNonWhitespace])
                        ? nextNonWhitespace : classified[nextNonWhitespace]!.AnchorTokenIndex;
                }
                else
                {
                    placement = SqlTriviaPlacement.Standalone;
                    anchor = null;
                }

                classified[index] = new SqlCommentTrivia(index, span, token.Text,
                    token.TokenType == TSqlTokenType.SingleLineComment
                        ? SqlCommentKind.Line : SqlCommentKind.Block,
                    placement, anchor);
                nextNonWhitespace = index;
            }
            else if (IsMeaningful(token))
            {
                nextNonWhitespace = index;
            }
        }

        var comments = new List<SqlCommentTrivia>();
        foreach (var item in classified)
        {
            if (item is not null) comments.Add(item);
        }

        return comments.AsReadOnly();
    }

    private static int LineAtEnd(SqlParseResult result, TSqlParserToken token)
    {
        var last = token.Text.Length == 0 ? token.Offset : token.Offset + token.Text.Length - 1;
        return result.LineMap.GetLinePosition(last).Line;
    }

    private static bool IsAdjacent(SqlParseResult result, TSqlParserToken first, TSqlParserToken next)
    {
        var end = first.Offset + first.Text.Length;
        var endLine = result.LineMap.GetLinePosition(end).Line;
        var nextLine = result.LineMap.GetLinePosition(next.Offset).Line;
        return nextLine - endLine <= 1;
    }

    private static bool IsComment(TSqlParserToken token) =>
        token.TokenType == TSqlTokenType.SingleLineComment
        || token.TokenType == TSqlTokenType.MultilineComment;

    private static bool IsMeaningful(TSqlParserToken token) =>
        token.TokenType != TSqlTokenType.WhiteSpace
        && token.TokenType != TSqlTokenType.EndOfFile
        && !IsComment(token);
}
