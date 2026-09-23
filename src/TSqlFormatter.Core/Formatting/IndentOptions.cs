namespace TSqlFormatter.Core.Formatting;

public sealed class IndentOptions
{
    public IndentOptions(int size = 4, bool useTabs = false)
    {
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        Size = size;
        UseTabs = useTabs;
    }

    public int Size { get; }

    public bool UseTabs { get; }
}
