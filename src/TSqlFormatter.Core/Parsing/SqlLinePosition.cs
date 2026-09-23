namespace TSqlFormatter.Core.Parsing;

/// <summary>A one-based line and column in the original SQL source.</summary>
public readonly struct SqlLinePosition
{
    public SqlLinePosition(int line, int column)
    {
        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}
