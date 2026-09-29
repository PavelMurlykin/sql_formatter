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
        QueryClauseOptions? clauses = null,
        JoinOptions? joins = null,
        WhereOptions? where = null,
        AlignmentOptions? alignment = null,
        RuleOptions? rules = null)
    {
        General = general ?? new GeneralOptions();
        Indent = indent ?? new IndentOptions();
        Keywords = keywords ?? new KeywordOptions();
        Select = select ?? new SelectOptions();
        Clauses = clauses ?? new QueryClauseOptions();
        Joins = joins ?? new JoinOptions();
        Where = where ?? new WhereOptions();
        Alignment = alignment ?? new AlignmentOptions();
        Rules = rules ?? new RuleOptions(RuleCatalog.Default);
    }

    public GeneralOptions General { get; }

    public IndentOptions Indent { get; }

    public KeywordOptions Keywords { get; }

    public SelectOptions Select { get; }

    public QueryClauseOptions Clauses { get; }

    public JoinOptions Joins { get; }

    public WhereOptions Where { get; }

    public AlignmentOptions Alignment { get; }

    public RuleOptions Rules { get; }

    /// <summary>Creates a new option set, replacing only the supplied sections.</summary>
    public FormattingOptions With(
        GeneralOptions? general = null,
        IndentOptions? indent = null,
        KeywordOptions? keywords = null,
        SelectOptions? select = null,
        QueryClauseOptions? clauses = null,
        JoinOptions? joins = null,
        WhereOptions? where = null,
        AlignmentOptions? alignment = null,
        RuleOptions? rules = null)
    {
        return new FormattingOptions(
            general ?? General,
            indent ?? Indent,
            keywords ?? Keywords,
            select ?? Select,
            clauses ?? Clauses,
            joins ?? Joins,
            where ?? Where,
            alignment ?? Alignment,
            rules ?? Rules);
    }
}
