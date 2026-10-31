using Newtonsoft.Json.Linq;
using TSqlFormatter.Core.History;

namespace TSqlFormatter.Configuration;

/// <summary>Per-user editor features; kept separate from named formatting profiles.</summary>
public sealed class EditorFeaturesSettings
{
    public EditorFeaturesSettings(AnalysisConfiguration? analysis = null, bool historyEnabled = true,
        int historyMaxSnapshots = 100, int historyMaxCharacters = 1_000_000, int historyMaxAgeDays = 0)
    {
        if (historyMaxSnapshots is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(historyMaxSnapshots));
        if (historyMaxCharacters is < 1 or > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(historyMaxCharacters));
        if (historyMaxAgeDays is < 0 or > 36500) throw new ArgumentOutOfRangeException(nameof(historyMaxAgeDays));
        Analysis = analysis ?? AnalysisConfiguration.Default;
        HistoryEnabled = historyEnabled; HistoryMaxSnapshots = historyMaxSnapshots;
        HistoryMaxCharacters = historyMaxCharacters; HistoryMaxAgeDays = historyMaxAgeDays;
    }

    public AnalysisConfiguration Analysis { get; }
    public bool HistoryEnabled { get; }
    public int HistoryMaxSnapshots { get; }
    public int HistoryMaxCharacters { get; }
    public int HistoryMaxAgeDays { get; }

    public EditorSnapshotStore CreateHistoryStore(string? directory = null) => new(directory)
    {
        Enabled = HistoryEnabled, MaxSnapshots = HistoryMaxSnapshots,
        MaxSnapshotCharacters = HistoryMaxCharacters, MaxAgeDays = HistoryMaxAgeDays
    };

    public string Serialize() => new JObject
    {
        ["version"] = 1,
        ["analysis"] = new JObject
        {
            ["enabled"] = Analysis.Enabled,
            ["disabled"] = new JArray(Analysis.DisabledRules),
            ["severity"] = new JObject(Analysis.SeverityOverrides.Select(p => new JProperty(p.Key, p.Value.ToString().ToLowerInvariant())))
        },
        ["history"] = new JObject
        {
            ["enabled"] = HistoryEnabled, ["maxSnapshots"] = HistoryMaxSnapshots,
            ["maxCharacters"] = HistoryMaxCharacters, ["maxAgeDays"] = HistoryMaxAgeDays
        }
    }.ToString();

    public static EditorFeaturesSettings Deserialize(string? json, bool legacyHistoryEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(historyEnabled: legacyHistoryEnabled);
        var root = JObject.Parse(json!);
        if (root["version"]?.Value<int>() != 1) throw new ArgumentException("Unsupported editor settings version.", nameof(json));
        var analysis = new AnalysisConfigurationParser().Parse(new JObject { ["analysis"] = root["analysis"]?.DeepClone() ?? new JObject() }.ToString());
        if (!analysis.Succeeded) throw new ArgumentException(analysis.Diagnostics[0].Message, nameof(json));
        var history = root["history"] as JObject ?? throw new ArgumentException("Missing history settings.", nameof(json));
        return new(analysis.Configuration, history.Value<bool?>("enabled") ?? legacyHistoryEnabled,
            history.Value<int?>("maxSnapshots") ?? 100, history.Value<int?>("maxCharacters") ?? 1_000_000,
            history.Value<int?>("maxAgeDays") ?? 0);
    }
}
