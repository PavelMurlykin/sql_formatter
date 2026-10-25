using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Navigation;

/// <summary>One outline entry with its source span; children nest under it.</summary>
public sealed class ScriptOutlineNode
{
    private readonly List<ScriptOutlineNode> children = new();

    public ScriptOutlineNode(ScriptOutlineNodeKind kind, string title, SqlTextSpan span)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("The outline title must not be empty.", nameof(title));
        }

        Kind = kind;
        Title = title;
        Span = span;
    }

    public ScriptOutlineNodeKind Kind { get; }

    public string Title { get; }

    /// <summary>Navigation target: the meaningful start of the node (the name, not the keyword).</summary>
    public SqlTextSpan Span { get; }

    public IReadOnlyList<ScriptOutlineNode> Children => children;

    internal void Add(ScriptOutlineNode child) => children.Add(child);
}
