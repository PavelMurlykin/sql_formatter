namespace TSqlFormatter.Core.Layout;

/// <summary>Increases indentation of broken lines within its content.</summary>
public sealed class IndentDoc : Doc
{
    public IndentDoc(int levels, Doc content)
    {
        if (levels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(levels));
        }

        Levels = levels;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public int Levels { get; }

    public Doc Content { get; }
}
