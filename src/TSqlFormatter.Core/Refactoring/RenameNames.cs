using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Refactoring;

/// <summary>Validation of new names for rename refactorings.</summary>
public static class RenameNames
{
    /// <summary>Variables must start with @ followed by an identifier body.</summary>
    public static bool IsValidVariableName(string? name) =>
        SqlIdentifierText.IsVariableName(name);

    /// <summary>Aliases are plain identifiers of at most 128 characters, excluding reserved SQL words.</summary>
    public static bool IsValidIdentifier(string? name) =>
        SqlIdentifierText.IsPlainIdentifier(name);
}
