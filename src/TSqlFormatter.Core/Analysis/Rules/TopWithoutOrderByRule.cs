using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Analysis.Rules;

/// <summary>
/// TSQL003: reports <c>TOP</c> in a query that has no <c>ORDER BY</c> of its own;
/// an enclosing <c>ORDER BY</c> of a set operation does not make the TOP rows deterministic.
/// </summary>
public sealed class TopWithoutOrderByRule : AnalysisRuleBase<QuerySpecification>
{
    public const string RuleCode = "TSQL003";

    public override string Code => RuleCode;

    public override AnalysisSeverity DefaultSeverity => AnalysisSeverity.Warning;

    protected override void Inspect(QuerySpecification fragment, List<AnalysisFinding> findings, CancellationToken cancellationToken)
    {
        if (fragment.TopRowFilter is not null && fragment.OrderByClause is null)
        {
            findings.Add(Finding(fragment.TopRowFilter, "TOP is used without ORDER BY; the row selection is not deterministic."));
        }
    }
}
