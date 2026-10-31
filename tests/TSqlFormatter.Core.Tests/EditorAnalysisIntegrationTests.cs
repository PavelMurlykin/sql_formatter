using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Analysis;
using TSqlFormatter.IdeShared;

namespace TSqlFormatter.Core.Tests;

public sealed class EditorAnalysisIntegrationTests
{
    private static string CreateFile(string? configuration)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tsql-analysis-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "query.sql");
        File.WriteAllText(path, "SELECT * FROM T;");
        if (configuration != null) File.WriteAllText(Path.Combine(dir, ".tsqlformatter.json"), configuration);
        return path;
    }

    [Fact]
    public void Editor_uses_saved_defaults_when_project_has_no_analysis_section()
    {
        var defaults = new AnalysisConfiguration(new[] { "TSQL001" },
            new Dictionary<string, AnalysisSeverity> { ["TSQL004"] = AnalysisSeverity.Info });
        var result = SqlDocumentAnalysis.Run(CreateFile("{\"version\":2}"),
            "SELECT * FROM T; UPDATE T SET A=1;", CancellationToken.None, defaults);
        Assert.Null(result.Error);
        var finding = Assert.Single(result.Diagnostics);
        Assert.Equal("TSQL004", finding.Code); Assert.Equal(AnalysisSeverity.Info, finding.Severity);
    }

    [Fact]
    public void Project_can_override_disabled_global_analysis_and_rule_settings()
    {
        var defaults = new AnalysisConfiguration(new[] { "TSQL001" }, new Dictionary<string, AnalysisSeverity>(), false);
        var path = CreateFile("""{"version":2,"analysis":{"enabled":true,"disabled":[],"severity":{"TSQL001":"error"}}}""");
        var result = SqlDocumentAnalysis.Run(path, "SELECT * FROM T;", CancellationToken.None, defaults);
        Assert.Null(result.Error);
        Assert.Equal(AnalysisSeverity.Error, Assert.Single(result.Diagnostics).Severity);
    }

    [Fact]
    public void Disabled_editor_analysis_does_not_parse_or_modify_the_document()
    {
        var defaults = new AnalysisConfiguration(Array.Empty<string>(), new Dictionary<string, AnalysisSeverity>(), false);
        var result = SqlDocumentAnalysis.Run(CreateFile(null), "SELECT FROM;", CancellationToken.None, defaults);
        Assert.Null(result.Error); Assert.Empty(result.Diagnostics); Assert.NotNull(result.LineMap); Assert.False(result.Enabled);
    }
}
