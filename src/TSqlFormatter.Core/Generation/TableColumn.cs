namespace TSqlFormatter.Core.Generation;

/// <summary>One column of a parsed CREATE TABLE statement.</summary>
public sealed class TableColumn
{
    public TableColumn(string name, string dataType, bool isNullable, bool isIdentity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The column name must not be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(dataType))
        {
            throw new ArgumentException("The column data type must not be empty.", nameof(dataType));
        }

        Name = name;
        DataType = dataType;
        IsNullable = isNullable;
        IsIdentity = isIdentity;
    }

    public string Name { get; }

    /// <summary>Full type text, for example NVARCHAR(50) or DECIMAL(18, 2).</summary>
    public string DataType { get; }

    public bool IsNullable { get; }

    public bool IsIdentity { get; }
}
