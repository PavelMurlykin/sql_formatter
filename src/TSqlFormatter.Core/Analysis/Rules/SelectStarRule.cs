using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Analysis.Rules;

/// <summary>TSQL001: reports <c>SELECT *</c> and qualified stars in any query, including subqueries.</summary>
public sealed class SelectStarRule : AnalysisRuleBase<QuerySpecification>
{
    public const string RuleCode = "TSQL001";

    public override string Code => RuleCode;

    public override AnalysisSeverity DefaultSeverity => AnalysisSeverity.Warning;

    protected override void Inspect(QuerySpecification fragment, List<AnalysisFinding> findings, CancellationToken cancellationToken)
    {
        foreach (var element in fragment.SelectElements)
        {
            if (element is SelectStarExpression)
            {
                findings.Add(Finding(element, "SELECT * is used; list the required columns explicitly."));
            }
        }
    }
}
