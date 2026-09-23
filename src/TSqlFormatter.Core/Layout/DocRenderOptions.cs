namespace TSqlFormatter.Core.Layout;

public sealed class DocRenderOptions
{
    public DocRenderOptions(
        int maxLineWidth = 100,
        int indentWidth = 4,
        DocLineEnding lineEnding = DocLineEnding.Lf,
        bool finalNewline = false,
        bool useTabs = false)
    {
        if (maxLineWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLineWidth));
        }

        if (indentWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(indentWidth));
        }

        if (!Enum.IsDefined(typeof(DocLineEnding), lineEnding))
        {
            throw new ArgumentOutOfRangeException(nameof(lineEnding));
        }

        MaxLineWidth = maxLineWidth;
        IndentWidth = indentWidth;
        LineEnding = lineEnding;
        FinalNewline = finalNewline;
        UseTabs = useTabs;
    }

    public int MaxLineWidth { get; }

    public int IndentWidth { get; }

    public DocLineEnding LineEnding { get; }

    public bool FinalNewline { get; }

    public bool UseTabs { get; }

    internal string Newline => LineEnding switch
    {
        DocLineEnding.Lf => "\n",
        DocLineEnding.CrLf => "\r\n",
        DocLineEnding.Cr => "\r",
        _ => throw new InvalidOperationException("Unsupported line ending.")
    };
}
