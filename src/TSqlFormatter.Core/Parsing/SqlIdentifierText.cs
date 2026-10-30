using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Parsing;

/// <summary>SQL spelling and validation for generated identifiers; variable names cannot be quoted.</summary>
internal static class SqlIdentifierText
{
    internal const int MaxLength = 128;

    internal static bool IsPlainIdentifier(string? name)
    {
        if (name is null || name.Length == 0 || name.Length > MaxLength || !IsBody(name, 0))
        {
            return false;
        }

        // Use ScriptDom's identifier validation rather than a hand-maintained
        // keyword list or parsing a document in the rename dialog's UI thread.
        return new TSql180Parser(true).ValidateIdentifier(name);
    }

    internal static bool IsVariableName(string? name) =>
        name is not null && name.Length > 1 && name.Length <= MaxLength &&
        name[0] == '@' && IsBody(name, 1);

    internal static string Format(string name) =>
        IsPlainIdentifier(name) ? name : "[" + name.Replace("]", "]]") + "]";

    private static bool IsBody(string value, int start)
    {
        if (!char.IsLetter(value[start]) && value[start] != '_' && value[start] != '#')
        {
            return false;
        }

        for (var index = start + 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsLetterOrDigit(character) && character != '_' && character != '#')
            {
                return false;
            }
        }

        return true;
    }
}
