using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Analysis.Rules;

/// <summary>TSQL002: reports <c>INSERT</c> statements without an explicit column list.</summary>
public sealed class InsertWithoutColumnListRule : AnalysisRuleBase<InsertStatement>
{
    public const string RuleCode = "TSQL002";

    public override string Code => RuleCode;

    public override AnalysisSeverity DefaultSeverity => AnalysisSeverity.Warning;

    protected override void Inspect(InsertStatement fragment, List<AnalysisFinding> findings, CancellationToken cancellationToken)
    {
        var specification = fragment.InsertSpecification;
        if (specification.InsertSource is ValuesInsertSource { IsDefaultValues: true })
        {
            return;
        }

        if (specification.InsertSource is null || specification.Columns is { Count: > 0 })
        {
            return;
        }

        findings.Add(Finding(specification, "INSERT without an explicit column list."));
    }
}
