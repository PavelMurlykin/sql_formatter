namespace TSqlFormatter.Core.Formatting;

/// <summary>Immutable, sectioned options for a future SQL formatter implementation.</summary>
public sealed class FormattingOptions
{
    public FormattingOptions(
        GeneralOptions? general = null,
        IndentOptions? indent = null,
        KeywordOptions? keywords = null)
    {
        General = general ?? new GeneralOptions();
        Indent = indent ?? new IndentOptions();
        Keywords = keywords ?? new KeywordOptions();
    }

    public GeneralOptions General { get; }

    public IndentOptions Indent { get; }

    public KeywordOptions Keywords { get; }
}
