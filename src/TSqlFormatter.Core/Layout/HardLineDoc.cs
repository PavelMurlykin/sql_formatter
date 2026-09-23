namespace TSqlFormatter.Core.Layout;

/// <summary>An unconditional newline.</summary>
public sealed class HardLineDoc : Doc
{
    private HardLineDoc()
    {
    }

    public static HardLineDoc Instance { get; } = new();
}
