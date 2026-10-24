namespace TSqlFormatter.Core.Analysis;

/// <summary>Per-run analysis settings: disabled rules and severity overrides.</summary>
public sealed class AnalysisOptions
{
    /// <summary>Returns a fresh options instance with all default rules enabled.</summary>
    public static AnalysisOptions Default => new AnalysisOptions();

    /// <summary>Rule codes that must not run; compared case-insensitively.</summary>
    public ISet<string> DisabledRules { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-rule severity overrides applied over the rule default; compared case-insensitively.</summary>
    public IDictionary<string, AnalysisSeverity> SeverityOverrides { get; } =
        new Dictionary<string, AnalysisSeverity>(StringComparer.OrdinalIgnoreCase);

    public bool IsRuleEnabled(string code) => !DisabledRules.Contains(code);

    public AnalysisSeverity GetSeverity(string code, AnalysisSeverity defaultSeverity) =>
        SeverityOverrides.TryGetValue(code, out var severity) ? severity : defaultSeverity;
}
