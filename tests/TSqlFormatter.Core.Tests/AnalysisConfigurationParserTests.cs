using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Analysis;

namespace TSqlFormatter.Core.Tests;

public sealed class AnalysisConfigurationParserTests
{
    private readonly AnalysisConfigurationParser _parser = new();

    [Fact]
    public void Missing_section_returns_default()
    {
        var result = _parser.Parse("{\"version\":1}");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Configuration!.DisabledRules);
        Assert.Empty(result.Configuration.SeverityOverrides);
        Assert.Same(AnalysisConfiguration.Default, result.Configuration);
    }

    [Fact]
    public void Parses_disabled_rules_and_severity_overrides()
    {
        var result = _parser.Parse(
            "{\"analysis\":{\"disabled\":[\"TSQL003\", \" tsql001 \"],\"severity\":{\"TSQL004\":\"warning\",\"tsql005\":\"ERROR\"}}}");

        Assert.True(result.Succeeded);
        var configuration = result.Configuration!;
        Assert.Equal(new[] { "TSQL003", "tsql001" }, configuration.DisabledRules);
        Assert.Equal(2, configuration.SeverityOverrides.Count);

        var options = configuration.ToAnalysisOptions();
        Assert.False(options.IsRuleEnabled("TSQL003"));
        Assert.False(options.IsRuleEnabled("tsql001"));
        Assert.True(options.IsRuleEnabled("TSQL002"));
        Assert.Equal(AnalysisSeverity.Warning, options.GetSeverity("TSQL004", AnalysisSeverity.Error));
        Assert.Equal(AnalysisSeverity.Error, options.GetSeverity("TSQL005", AnalysisSeverity.Error));
        Assert.Equal(AnalysisSeverity.Warning, options.GetSeverity("TSQL001", AnalysisSeverity.Warning));
    }

    [Fact]
    public void Severity_accepts_any_casing()
    {
        var result = _parser.Parse("{\"analysis\":{\"severity\":{\"TSQL001\":\"InFo\"}}}");

        Assert.True(result.Succeeded);
        Assert.Equal(AnalysisSeverity.Info,
            result.Configuration!.ToAnalysisOptions().GetSeverity("TSQL001", AnalysisSeverity.Warning));
    }

    [Fact]
    public void Unknown_analysis_property_is_rejected()
    {
        var result = _parser.Parse("{\"analysis\":{\"enabled\":true}}");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "TSF2000" &&
                diagnostic.Message.Contains("analysis.enabled"));
    }

    [Fact]
    public void Invalid_severity_value_is_rejected()
    {
        var result = _parser.Parse("{\"analysis\":{\"severity\":{\"TSQL004\":\"fatal\"}}}");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("info, warning, error"));
    }

    [Theory]
    [InlineData("{\"analysis\":{\"disabled\":\"TSQL001\"}}")]
    [InlineData("{\"analysis\":{\"disabled\":[\"\"]}}")]
    [InlineData("{\"analysis\":{\"severity\":[\"TSQL004\"]}}")]
    [InlineData("{\"analysis\":{\"severity\":{\"TSQL004\":3}}}")]
    [InlineData("{\"analysis\":\"yes\"}")]
    [InlineData("{not json")]
    public void Malformed_section_is_rejected(string json)
    {
        var result = _parser.Parse(json);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("TSF2000", diagnostic.Code));
    }

    [Fact]
    public void Version1_configuration_with_analysis_section_still_parses()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var json = "{\"version\":1,\"analysis\":{\"disabled\":[\"TSQL001\"]}}";

        var formatting = serializer.Parse(json);
        var analysis = _parser.Parse(json);

        Assert.True(formatting.Succeeded);
        Assert.True(analysis.Succeeded);
        Assert.Equal(new[] { "TSQL001" }, analysis.Configuration!.DisabledRules);
    }

    [Fact]
    public void Version2_configuration_with_analysis_section_still_parses()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var json = "{\"version\":2,\"analysis\":{\"severity\":{\"TSQL004\":\"warning\"}}}";

        var formatting = serializer.Parse(json);
        var analysis = _parser.Parse(json);

        Assert.True(formatting.Succeeded);
        Assert.True(analysis.Succeeded);
        Assert.Equal(AnalysisSeverity.Warning,
            analysis.Configuration!.ToAnalysisOptions().GetSeverity("TSQL004", AnalysisSeverity.Error));
    }
}
