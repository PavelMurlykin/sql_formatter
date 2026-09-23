namespace TSqlFormatter.Core.Layout;

/// <summary>Content that a renderer may keep flat if it fits the available width.</summary>
public sealed class GroupDoc : Doc
{
    public GroupDoc(Doc content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public Doc Content { get; }
}
