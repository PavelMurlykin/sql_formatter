using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Analysis.Rules;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Analysis;

/// <summary>Explicit destructive-statement check, independent of optional analysis settings.</summary>
public sealed class DangerousSqlAnalyzer
{
    public IReadOnlyList<AnalysisDiagnostic> Analyze(SqlParseResult parsed, CancellationToken cancellationToken = default)
    {
        if (parsed == null) throw new ArgumentNullException(nameof(parsed));
        cancellationToken.ThrowIfCancellationRequested();
        if (!parsed.ParseSucceeded) throw new ArgumentException("Dangerous SQL checking requires a successfully parsed script.", nameof(parsed));
        var findings = new SqlAnalyzer(new IAnalysisRule[] { new UpdateWithoutWhereRule(), new DeleteWithoutWhereRule() })
            .Analyze(parsed, cancellationToken: cancellationToken).Diagnostics.ToList();
        parsed.Root!.Accept(new Visitor(parsed, findings, cancellationToken));
        return findings.OrderBy(d => d.Span.StartOffset).ToArray();
    }
    private sealed class Visitor : TSqlFragmentVisitor
    {
        private readonly SqlParseResult parsed;
        private readonly List<AnalysisDiagnostic> findings;
        private readonly CancellationToken cancellationToken;
        internal Visitor(SqlParseResult parsed, List<AnalysisDiagnostic> findings, CancellationToken cancellationToken)
        { this.parsed = parsed; this.findings = findings; this.cancellationToken = cancellationToken; }
        public override void Visit(TSqlFragment node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is not TSqlStatement) return;
            string? code = null, message = null;
            if (node.GetType().Name.StartsWith("Drop", StringComparison.Ordinal))
            { code = "TSQL006"; message = "DROP statement removes a database object."; }
            else if (node is TruncateTableStatement)
            { code = "TSQL007"; message = "TRUNCATE TABLE removes rows without a WHERE clause."; }
            if (code == null) return;
            var position = parsed.LineMap.GetLinePosition(node.StartOffset);
            findings.Add(new AnalysisDiagnostic(code, AnalysisSeverity.Error, message!,
                new SqlTextSpan(node.StartOffset, node.FragmentLength), position.Line, position.Column));
        }
    }
}
