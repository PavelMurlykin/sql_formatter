using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Enumerates an AST with ScriptDom's visitor mechanism.</summary>
public sealed class SqlFragmentWalker
{
    public IReadOnlyList<TSqlFragment> Walk(TSqlFragment root, CancellationToken cancellationToken = default)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var visitor = new CollectingVisitor(cancellationToken);
        root.Accept(visitor);
        return Array.AsReadOnly(visitor.Nodes.ToArray());
    }

    private sealed class CollectingVisitor : TSqlFragmentVisitor
    {
        private readonly CancellationToken _cancellationToken;

        public CollectingVisitor(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public List<TSqlFragment> Nodes { get; } = new();

        public override void Visit(TSqlFragment node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Nodes.Add(node);
        }
    }
}
