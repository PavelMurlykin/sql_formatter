using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Generation;

/// <summary>A table shape parsed from a CREATE TABLE statement, with the statement span.</summary>
public sealed class SqlTableDefinition
{
    public SqlTableDefinition(
        string schema,
        string name,
        IReadOnlyList<TableColumn> columns,
        SqlTextSpan statementSpan)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The table name must not be empty.", nameof(name));
        }

        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        Schema = schema ?? string.Empty;
        Name = name;
        Columns = columns;
        StatementSpan = statementSpan;
    }

    public string Schema { get; }

    public string Name { get; }

    public IReadOnlyList<TableColumn> Columns { get; }

    /// <summary>Span of the CREATE TABLE statement in the analyzed source.</summary>
    public SqlTextSpan StatementSpan { get; }

    public string DisplayName => Schema.Length > 0 ? Schema + "." + Name : Name;

    public string SchemaOrDbo => Schema.Length > 0 ? Schema : "dbo";
}
