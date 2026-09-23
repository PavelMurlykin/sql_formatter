using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting;

public sealed class GeneralOptions
{
    public GeneralOptions(
        int maxLineWidth = 100,
        DocLineEnding lineEnding = DocLineEnding.Lf,
        bool finalNewline = false)
    {
        if (maxLineWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLineWidth));
        }

        if (!Enum.IsDefined(typeof(DocLineEnding), lineEnding))
        {
            throw new ArgumentOutOfRangeException(nameof(lineEnding));
        }

        MaxLineWidth = maxLineWidth;
        LineEnding = lineEnding;
        FinalNewline = finalNewline;
    }

    public int MaxLineWidth { get; }

    public DocLineEnding LineEnding { get; }

    public bool FinalNewline { get; }
}
