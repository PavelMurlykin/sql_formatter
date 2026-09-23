namespace TSqlFormatter.Core.Formatting;

public sealed class KeywordOptions
{
    public KeywordOptions(KeywordCase @case = KeywordCase.Preserve)
    {
        if (!Enum.IsDefined(typeof(KeywordCase), @case))
        {
            throw new ArgumentOutOfRangeException(nameof(@case));
        }

        Case = @case;
    }

    public KeywordCase Case { get; }
}
