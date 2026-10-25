using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Navigation;

/// <summary>
/// Locates the declaration of a local @variable for an offset inside the same
/// batch: the nearest declaration at or above the reference, or null.
/// </summary>
public sealed class SymbolLocator
{
    public SqlTextSpan? FindVariableDeclaration(SqlParseResult parseResult, int offset)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        if (offset < 0 || offset > parseResult.Source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (parseResult.Root is null)
        {
            return null;
        }

        var token = FindTokenAt(parseResult, offset);
        if (token is null || token.TokenType != TSqlTokenType.Variable)
        {
            return null;
        }

        var nodes = new SqlFragmentWalker().Walk(parseResult.Root);
        var batch = nodes.OfType<TSqlBatch>()
            .Where(batch => batch.StartOffset <= token.Offset &&
                token.Offset + token.Text.Length <= batch.StartOffset + batch.FragmentLength)
            .OrderBy(batch => batch.FragmentLength)
            .FirstOrDefault();
        if (batch is null)
        {
            return null;
        }

        SqlTextSpan? nearest = null;
        foreach (var declaration in nodes.OfType<DeclareVariableElement>())
        {
            var variableName = declaration.VariableName;
            if (variableName is null ||
                declaration.StartOffset < batch.StartOffset ||
                declaration.StartOffset + declaration.FragmentLength > batch.StartOffset + batch.FragmentLength ||
                !string.Equals(variableName.Value, token.Text, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var span = new SqlTextSpan(variableName.StartOffset, variableName.FragmentLength);
            if (variableName.StartOffset > offset)
            {
                continue;
            }

            if (nearest is null || span.StartOffset > nearest.Value.StartOffset)
            {
                nearest = span;
            }
        }

        return nearest;
    }

    private static TSqlParserToken? FindTokenAt(SqlParseResult parseResult, int offset)
    {
        foreach (var token in parseResult.Tokens)
        {
            if (token.Offset <= offset && offset < token.Offset + token.Text.Length)
            {
                return token;
            }
        }

        return null;
    }
}
