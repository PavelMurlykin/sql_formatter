namespace TSqlFormatter.Core.Layout;

/// <summary>A space in flat mode or a newline in broken mode.</summary>
public sealed class SoftLineDoc : Doc
{
    private SoftLineDoc()
    {
    }

    public static SoftLineDoc Instance { get; } = new();
}
