using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Analysis.Rules;

/// <summary>TSQL005: reports <c>DELETE</c> statements without a <c>WHERE</c> clause.</summary>
public sealed class DeleteWithoutWhereRule : AnalysisRuleBase<DeleteStatement>
{
    public const string RuleCode = "TSQL005";

    public override string Code => RuleCode;

    public override AnalysisSeverity DefaultSeverity => AnalysisSeverity.Error;

    protected override void Inspect(DeleteStatement fragment, List<AnalysisFinding> findings, CancellationToken cancellationToken)
    {
        if (fragment.DeleteSpecification.WhereClause is null)
        {
            findings.Add(Finding(fragment.DeleteSpecification, "DELETE without a WHERE clause."));
        }
    }
}
