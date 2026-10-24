using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Analysis;

/// <summary>
/// Base class for rules that inspect every AST node of one fragment type.
/// Derive, override <see cref="Inspect"/> and append <see cref="Finding"/> results.
/// </summary>
public abstract class AnalysisRuleBase<TFragment> : IAnalysisRule
    where TFragment : TSqlFragment
{
    public abstract string Code { get; }

    public abstract AnalysisSeverity DefaultSeverity { get; }

    public IEnumerable<AnalysisFinding> Analyze(TSqlFragment root, CancellationToken cancellationToken = default)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var visitor = new Visitor(this, cancellationToken);
        root.Accept(visitor);
        return visitor.Findings;
    }

    protected abstract void Inspect(TFragment fragment, List<AnalysisFinding> findings, CancellationToken cancellationToken);

    protected static AnalysisFinding Finding(TSqlFragment fragment, string message) =>
        new AnalysisFinding(new SqlTextSpan(fragment.StartOffset, fragment.FragmentLength), message);

    private sealed class Visitor : TSqlFragmentVisitor
    {
        private readonly AnalysisRuleBase<TFragment> _owner;
        private readonly CancellationToken _cancellationToken;

        public Visitor(AnalysisRuleBase<TFragment> owner, CancellationToken cancellationToken)
        {
            _owner = owner;
            _cancellationToken = cancellationToken;
        }

        public List<AnalysisFinding> Findings { get; } = new List<AnalysisFinding>();

        public override void Visit(TSqlFragment node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (node is TFragment fragment)
            {
                _owner.Inspect(fragment, Findings, _cancellationToken);
            }
        }
    }
}
