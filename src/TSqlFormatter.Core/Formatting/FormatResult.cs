namespace TSqlFormatter.Core.Formatting;

public sealed class FormatResult
{
    public FormatResult(
        string text,
        bool changed,
        bool parseSucceeded,
        IReadOnlyList<TextEdit>? edits = null,
        IReadOnlyList<FormatterDiagnostic>? diagnostics = null)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Changed = changed;
        ParseSucceeded = parseSucceeded;
        Edits = Snapshot(edits, nameof(edits));
        Diagnostics = Snapshot(diagnostics, nameof(diagnostics));
    }

    public string Text { get; }

    public IReadOnlyList<TextEdit> Edits { get; }

    public IReadOnlyList<FormatterDiagnostic> Diagnostics { get; }

    public bool Changed { get; }

    public bool ParseSucceeded { get; }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T>? items, string parameterName)
        where T : class
    {
        if (items is null || items.Count == 0)
        {
            return Array.Empty<T>();
        }

        var copy = new T[items.Count];
        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = items[index]
                ?? throw new ArgumentException("The collection cannot contain null entries.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}
