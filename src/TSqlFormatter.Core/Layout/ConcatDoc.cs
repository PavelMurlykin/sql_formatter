namespace TSqlFormatter.Core.Layout;

/// <summary>An ordered sequence of document nodes.</summary>
public sealed class ConcatDoc : Doc
{
    public ConcatDoc(IReadOnlyList<Doc> children)
    {
        if (children is null)
        {
            throw new ArgumentNullException(nameof(children));
        }

        var copy = new Doc[children.Count];
        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = children[index]
                ?? throw new ArgumentException("Children cannot contain null nodes.", nameof(children));
        }

        Children = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<Doc> Children { get; }
}
