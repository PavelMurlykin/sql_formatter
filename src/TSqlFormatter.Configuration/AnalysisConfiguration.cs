using TSqlFormatter.Core.Analysis;

namespace TSqlFormatter.Configuration;

/// <summary>The analysis section of .tsqlformatter.json: disabled rules and severity overrides.</summary>
public sealed class AnalysisConfiguration
{
    public static AnalysisConfiguration Default { get; } =
        new AnalysisConfiguration(Array.Empty<string>(), new Dictionary<string, AnalysisSeverity>());

    public AnalysisConfiguration(
        IReadOnlyList<string> disabledRules,
        IReadOnlyDictionary<string, AnalysisSeverity> severityOverrides,
        bool enabled = true)
    {
        if (disabledRules is null) throw new ArgumentNullException(nameof(disabledRules));
        if (severityOverrides is null) throw new ArgumentNullException(nameof(severityOverrides));

        Enabled = enabled;
        DisabledRules = disabledRules.ToArray();
        var overrides = new Dictionary<string, AnalysisSeverity>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in severityOverrides)
        {
            overrides[pair.Key] = pair.Value;
        }

        SeverityOverrides = overrides;
    }

    public IReadOnlyList<string> DisabledRules { get; }
    public bool Enabled { get; }

    public IReadOnlyDictionary<string, AnalysisSeverity> SeverityOverrides { get; }

    public AnalysisOptions ToAnalysisOptions()
    {
        var options = new AnalysisOptions();
        if (!Enabled)
            foreach (var rule in SqlAnalyzer.DefaultRules) options.DisabledRules.Add(rule.Code);
        foreach (var code in DisabledRules)
        {
            options.DisabledRules.Add(code);
        }

        foreach (var pair in SeverityOverrides)
        {
            options.SeverityOverrides[pair.Key] = pair.Value;
        }

        return options;
    }
}
