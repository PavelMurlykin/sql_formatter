using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Refactoring;

/// <summary>A symbol found under the caret and all its occurrences within the rename scope.</summary>
public sealed class RenameSymbol
{
    public RenameSymbol(RenameSymbolKind kind, string name, IReadOnlyList<SqlTextSpan> occurrences)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The symbol name must not be empty.", nameof(name));
        }

        if (occurrences is null)
        {
            throw new ArgumentNullException(nameof(occurrences));
        }

        if (occurrences.Count == 0)
        {
            throw new ArgumentException("The symbol requires at least one occurrence.", nameof(occurrences));
        }

        Kind = kind;
        Name = name;
        Occurrences = occurrences;
    }

    public RenameSymbolKind Kind { get; }

    /// <summary>Current name including the leading @ for variables.</summary>
    public string Name { get; }

    /// <summary>Spans to replace, sorted by offset and not overlapping.</summary>
    public IReadOnlyList<SqlTextSpan> Occurrences { get; }
}
