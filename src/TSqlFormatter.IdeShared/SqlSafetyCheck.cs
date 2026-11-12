using System;
using System.Collections.Generic;
using System.Threading;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Analysis;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.IdeShared;

/// <summary>One immutable context/text snapshot; caller runs parsing off the UI thread.</summary>
public sealed class SqlSafetyCheck
{
    private SqlSafetyCheck(SqlEnvironmentMatch environment, IReadOnlyList<AnalysisDiagnostic> diagnostics, string? error)
    { Environment = environment; Diagnostics = diagnostics; Error = error; }
    public SqlEnvironmentMatch Environment { get; }
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }
    public string? Error { get; }
    public static SqlSafetyCheck Run(string source, SqlEnvironmentContext context,
        SqlEnvironmentConfiguration environments, CancellationToken cancellationToken = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var match = environments.Match(context);
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto, cancellationToken);
        if (!parsed.ParseSucceeded) return new(match, Array.Empty<AnalysisDiagnostic>(), parsed.Diagnostics[0].Message);
        return new(match, new DangerousSqlAnalyzer().Analyze(parsed, cancellationToken), null);
    }
}
