using Microsoft.VisualStudio.Text;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.IdeShared;

/// <summary>Tracks the parameters of the last inserted snippet while the buffer snapshot is unchanged.</summary>
public sealed class SnippetInsertionSession
{
    private int index = -1;

    public SnippetInsertionSession(ITextSnapshot snapshot, IReadOnlyList<SqlTextSpan> parameters)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        if (parameters.Count == 0)
        {
            throw new ArgumentException("The session requires at least one parameter.", nameof(parameters));
        }
    }

    public ITextSnapshot Snapshot { get; }

    public IReadOnlyList<SqlTextSpan> Parameters { get; }

    public bool IsCurrent(ITextSnapshot current) => ReferenceEquals(current, Snapshot);

    /// <summary>Returns the parameters in navigation order, cycling back to the first after the last.</summary>
    public SqlTextSpan NextParameter()
    {
        index = (index + 1) % Parameters.Count;
        return Parameters[index];
    }
}
