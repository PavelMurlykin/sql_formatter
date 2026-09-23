namespace TSqlFormatter.Core.Parsing;

/// <summary>Converts between UTF-16 source offsets and one-based line/column positions.</summary>
public sealed class SqlLineMap
{
    private readonly int[] _lineStarts;
    private readonly int _sourceLength;

    public SqlLineMap(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        _sourceLength = source.Length;
        var lineStarts = new List<int> { 0 };
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\r')
            {
                if (index + 1 < source.Length && source[index + 1] == '\n')
                {
                    index++;
                }

                lineStarts.Add(index + 1);
            }
            else if (source[index] == '\n')
            {
                lineStarts.Add(index + 1);
            }
        }

        _lineStarts = lineStarts.ToArray();
    }

    public int LineCount => _lineStarts.Length;

    /// <summary>Returns the position of an offset, including the end-of-source offset.</summary>
    public SqlLinePosition GetLinePosition(int offset)
    {
        if (offset < 0 || offset > _sourceLength)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var lineIndex = Array.BinarySearch(_lineStarts, offset);
        if (lineIndex < 0)
        {
            lineIndex = ~lineIndex - 1;
        }

        return new SqlLinePosition(lineIndex + 1, offset - _lineStarts[lineIndex] + 1);
    }

    /// <summary>Returns the source offset of a one-based line and column.</summary>
    public int GetOffset(int line, int column)
    {
        if (line < 1 || line > _lineStarts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        var start = _lineStarts[line - 1];
        var lastOffset = line < _lineStarts.Length ? _lineStarts[line] - 1 : _sourceLength;
        var offset = (long)start + column - 1;
        if (column < 1 || offset > lastOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        return (int)offset;
    }

    public int GetOffset(SqlLinePosition position) => GetOffset(position.Line, position.Column);
}
