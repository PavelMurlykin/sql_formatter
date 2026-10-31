using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Analysis;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class EditorFeaturesSettingsTests
{
    [Fact]
    public void Settings_round_trip_preserves_enabled_rules_severities_and_history_limits()
    {
        var original = new EditorFeaturesSettings(new AnalysisConfiguration(new[] { "TSQL001" },
            new Dictionary<string, AnalysisSeverity> { ["TSQL004"] = AnalysisSeverity.Warning }, false),
            false, 250, 12345, 17);
        var restored = EditorFeaturesSettings.Deserialize(original.Serialize());
        Assert.False(restored.Analysis.Enabled);
        Assert.Contains("TSQL001", restored.Analysis.DisabledRules);
        Assert.Equal(AnalysisSeverity.Warning, restored.Analysis.SeverityOverrides["TSQL004"]);
        Assert.False(restored.HistoryEnabled);
        Assert.Equal(250, restored.HistoryMaxSnapshots);
        Assert.Equal(12345, restored.HistoryMaxCharacters);
        Assert.Equal(17, restored.HistoryMaxAgeDays);
        var store = restored.CreateHistoryStore();
        Assert.False(store.Enabled);
        Assert.Equal(250, store.MaxSnapshots);
        Assert.Equal(17, store.MaxAgeDays);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Empty_settings_migrate_the_legacy_history_switch(bool enabled)
    {
        var settings = EditorFeaturesSettings.Deserialize("", enabled);
        Assert.Equal(enabled, settings.HistoryEnabled);
        Assert.True(settings.Analysis.Enabled);
        Assert.Equal(0, settings.HistoryMaxAgeDays);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(10001, 10, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 16777217, 0)]
    [InlineData(1, 10, -1)]
    [InlineData(1, 10, 36501)]
    public void Invalid_retention_is_rejected(int count, int characters, int days) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorFeaturesSettings(
            historyMaxSnapshots: count, historyMaxCharacters: characters, historyMaxAgeDays: days));

    [Fact]
    public void Project_analysis_overrides_only_explicit_settings()
    {
        var defaults = new AnalysisConfiguration(new[] { "TSQL001" },
            new Dictionary<string, AnalysisSeverity> { ["TSQL004"] = AnalysisSeverity.Info }, false);
        var parser = new AnalysisConfigurationParser();
        var inherited = parser.Parse("{\"version\":2}", defaults).Configuration!;
        Assert.Same(defaults, inherited);
        var result = parser.Parse("""{"analysis":{"enabled":true,"severity":{"TSQL005":"warning"}}}""", defaults).Configuration!;
        Assert.True(result.Enabled);
        Assert.Contains("TSQL001", result.DisabledRules);
        Assert.Equal(AnalysisSeverity.Info, result.SeverityOverrides["TSQL004"]);
        Assert.Equal(AnalysisSeverity.Warning, result.SeverityOverrides["TSQL005"]);
        Assert.Empty(parser.Parse("""{"analysis":{"disabled":[]}}""", defaults).Configuration!.DisabledRules);
        Assert.False(defaults.Enabled);
    }

    [Theory]
    [InlineData("\"false\"")]
    [InlineData("0")]
    [InlineData("null")]
    public void Enabled_requires_a_json_boolean(string value) =>
        Assert.False(new AnalysisConfigurationParser().Parse("{\"analysis\":{\"enabled\":" + value + "}}").Succeeded);

    [Fact]
    public void Disabled_analysis_produces_no_rule_findings()
    {
        var configuration = new AnalysisConfiguration(Array.Empty<string>(), new Dictionary<string, AnalysisSeverity>(), false);
        var parsed = new ScriptDomSqlParser().Parse("SELECT * FROM T; DELETE FROM T;", SqlDialectVersion.Auto);
        Assert.Empty(new SqlAnalyzer().Analyze(parsed, configuration.ToAnalysisOptions()).Diagnostics);
    }
}
