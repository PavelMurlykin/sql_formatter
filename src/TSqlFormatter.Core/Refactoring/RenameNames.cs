namespace TSqlFormatter.Core.Refactoring;

/// <summary>Validation of new names for rename refactorings.</summary>
public static class RenameNames
{
    /// <summary>Variables must start with @ followed by an identifier body.</summary>
    public static bool IsValidVariableName(string? name) =>
        name is not null && name.Length > 1 && name[0] == '@' && IsValidBody(name, 1);

    /// <summary>Aliases are plain identifiers without a leading @.</summary>
    public static bool IsValidIdentifier(string? name) =>
        name is not null && name.Length > 0 && name[0] != '@' && IsValidBody(name, 0);

    private static bool IsValidBody(string value, int start)
    {
        var first = value[start];
        if (!char.IsLetter(first) && first != '_' && first != '#')
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
