namespace TSqlFormatter.Core.Parsing;

/// <summary>A character range in the original SQL source. The end offset is exclusive.</summary>
public readonly struct SqlTextSpan
{
    public SqlTextSpan(int startOffset, int length)
    {
        if (startOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        }

        if (length < 0 || length > int.MaxValue - startOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        StartOffset = startOffset;
        Length = length;
    }

    public int StartOffset { get; }

    public int Length { get; }

    public int EndOffset => StartOffset + Length;
}
