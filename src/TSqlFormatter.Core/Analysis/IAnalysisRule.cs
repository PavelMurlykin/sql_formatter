using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Analysis;

/// <summary>A single static-analysis rule that inspects a parsed T-SQL AST.</summary>
public interface IAnalysisRule
{
    /// <summary>Stable rule identifier, for example <c>TSQL001</c>.</summary>
    string Code { get; }

    AnalysisSeverity DefaultSeverity { get; }

    /// <summary>Returns raw findings for every matching node of the AST, in traversal order.</summary>
    IEnumerable<AnalysisFinding> Analyze(TSqlFragment root, CancellationToken cancellationToken = default);
}
