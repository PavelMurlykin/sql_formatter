using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Analysis.Rules;

/// <summary>TSQL004: reports <c>UPDATE</c> statements without a <c>WHERE</c> clause.</summary>
public sealed class UpdateWithoutWhereRule : AnalysisRuleBase<UpdateStatement>
{
    public const string RuleCode = "TSQL004";

    public override string Code => RuleCode;

    public override AnalysisSeverity DefaultSeverity => AnalysisSeverity.Error;

    protected override void Inspect(UpdateStatement fragment, List<AnalysisFinding> findings, CancellationToken cancellationToken)
    {
        if (fragment.UpdateSpecification.WhereClause is null)
        {
            findings.Add(Finding(fragment.UpdateSpecification, "UPDATE without a WHERE clause."));
        }
    }
}
