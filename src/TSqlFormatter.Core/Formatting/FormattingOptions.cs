namespace TSqlFormatter.Core.Formatting;

/// <summary>Immutable, sectioned options for SQL formatting.</summary>
public sealed class FormattingOptions
{
    /// <summary>Built-in formatting defaults shared by code and configuration loaders.</summary>
    public static FormattingOptions Default { get; } = new();

    public FormattingOptions(
        GeneralOptions? general = null,
        IndentOptions? indent = null,
        KeywordOptions? keywords = null,
        SelectOptions? select = null,
        QueryClauseOptions? clauses = null)
    {
        General = general ?? new GeneralOptions();
        Indent = indent ?? new IndentOptions();
        Keywords = keywords ?? new KeywordOptions();
        Select = select ?? new SelectOptions();
        Clauses = clauses ?? new QueryClauseOptions();
    }

    public GeneralOptions General { get; }

    public IndentOptions Indent { get; }

    public KeywordOptions Keywords { get; }

    public SelectOptions Select { get; }

    public QueryClauseOptions Clauses { get; }

    /// <summary>Creates a new option set, replacing only the supplied sections.</summary>
    public FormattingOptions With(
        GeneralOptions? general = null,
        IndentOptions? indent = null,
        KeywordOptions? keywords = null,
        SelectOptions? select = null,
        QueryClauseOptions? clauses = null)
    {
        return new FormattingOptions(
            general ?? General,
            indent ?? Indent,
            keywords ?? Keywords,
            select ?? Select,
            clauses ?? Clauses);
    }
}
