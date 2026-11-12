using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Analysis;
using TSqlFormatter.Core.Parsing;
using TSqlFormatter.IdeShared;

namespace TSqlFormatter.Core.Tests;

public sealed class DangerousSqlTests
{
    private static SqlParseResult Parse(string source)
    {
        var parsed = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto);
        Assert.True(parsed.ParseSucceeded, string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));
        return parsed;
    }
    [Theory]
    [InlineData("DROP TABLE dbo.T;")]
    [InlineData("DROP TABLE IF EXISTS dbo.T;")]
    [InlineData("DROP DATABASE ExampleDb;")]
    [InlineData("DROP VIEW dbo.V;")]
    [InlineData("DROP PROCEDURE dbo.P;")]
    [InlineData("DROP FUNCTION dbo.F;")]
    [InlineData("DROP INDEX IX_T ON dbo.T;")]
    [InlineData("DROP SCHEMA ExampleSchema;")]
    [InlineData("DROP TRIGGER dbo.Tr;")]
    [InlineData("DROP TYPE dbo.ExampleType;")]
    public void Drop_statements_are_detected_once(string sql)
    {
        var diagnostic = Assert.Single(new DangerousSqlAnalyzer().Analyze(Parse(sql)));
        Assert.Equal("TSQL006", diagnostic.Code); Assert.Equal(AnalysisSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line); Assert.Equal(1, diagnostic.Column);
    }
    [Theory]
    [InlineData("TRUNCATE TABLE dbo.T;", "TSQL007")]
    [InlineData("UPDATE TOP (1) dbo.T SET A=1;", "TSQL004")]
    [InlineData("DELETE FROM dbo.T;", "TSQL005")]
    public void Truncate_and_unfiltered_writes_are_detected(string sql, string code) =>
        Assert.Equal(code, Assert.Single(new DangerousSqlAnalyzer().Analyze(Parse(sql))).Code);

    [Theory]
    [InlineData("SELECT N'DROP TABLE dbo.T; DELETE FROM dbo.T;'; -- TRUNCATE TABLE dbo.T;")]
    [InlineData("SELECT [DROP], [TRUNCATE] FROM dbo.T;")]
    [InlineData("/* UPDATE T SET A=1; */ SELECT 1;")]
    [InlineData("UPDATE dbo.T SET A=1 WHERE Id=1; DELETE FROM dbo.T WHERE Id=2;")]
    [InlineData("EXEC(N'DROP TABLE dbo.T;');")]
    [InlineData("SELECT * FROM dbo.T; INSERT INTO dbo.T VALUES (1);")]
    public void Safe_statements_comments_identifiers_and_dynamic_sql_are_not_false_positives(string sql) =>
        Assert.Empty(new DangerousSqlAnalyzer().Analyze(Parse(sql)));

    [Fact]
    public void Findings_are_ordered_and_point_to_original_lines_columns_and_spans()
    {
        const string sql = "-- заголовок\r\n  DROP TABLE dbo.T;\r\nTRUNCATE TABLE dbo.T;\r\nUPDATE dbo.T SET A=1;\r\nDELETE FROM dbo.T;";
        var findings = new DangerousSqlAnalyzer().Analyze(Parse(sql));
        Assert.Equal(new[] { "TSQL006", "TSQL007", "TSQL004", "TSQL005" }, findings.Select(d => d.Code));
        Assert.Equal(new[] { 2, 3, 4, 5 }, findings.Select(d => d.Line));
        Assert.Equal(3, findings[0].Column); Assert.Equal(sql.IndexOf("DROP", StringComparison.Ordinal), findings[0].Span.StartOffset);
        Assert.StartsWith("DROP TABLE", sql.Substring(findings[0].Span.StartOffset, findings[0].Span.Length));
    }
    [Fact]
    public void Module_bodies_and_nested_control_flow_are_checked()
    {
        const string sql = "CREATE PROCEDURE dbo.P AS BEGIN IF 1=0 DROP TABLE dbo.T; WHILE 1=0 BEGIN DELETE FROM dbo.T; END; END;\nGO\nTRUNCATE TABLE dbo.T;";
        Assert.Equal(new[] { "TSQL006", "TSQL005", "TSQL007" }, new DangerousSqlAnalyzer().Analyze(Parse(sql)).Select(d => d.Code));
    }
    [Fact]
    public void Parse_errors_are_incomplete_checks_even_when_context_matches_prod()
    {
        var environments = new SqlEnvironmentConfiguration(new[] { new SqlEnvironmentRule(SqlEnvironmentKind.Prod, "production") });
        var check = SqlSafetyCheck.Run("DROP TABLE ; SELECT FROM;", new("q.sql", "production"), environments);
        Assert.NotNull(check.Error); Assert.Empty(check.Diagnostics); Assert.Equal(SqlEnvironmentKind.Prod, check.Environment.Kind);
        Assert.Throws<ArgumentException>(() => new DangerousSqlAnalyzer().Analyze(new ScriptDomSqlParser().Parse("SELECT FROM;", SqlDialectVersion.Auto)));
    }
    [Fact]
    public void Safety_check_is_independent_of_disabled_normal_analysis_and_does_not_infer_use_targets()
    {
        var settings = new EditorFeaturesSettings(new AnalysisConfiguration(new[] { "TSQL004", "TSQL005" },
            new Dictionary<string, AnalysisSeverity>(), false), environments: new SqlEnvironmentConfiguration(new[] {
                new SqlEnvironmentRule(SqlEnvironmentKind.Test, databaseContains: "test") }));
        var result = SqlSafetyCheck.Run("USE Production; UPDATE T SET A=1; DELETE FROM T;", new("q.sql", "server", "test"), settings.Environments);
        Assert.Null(result.Error); Assert.Equal(2, result.Diagnostics.Count); Assert.Equal(SqlEnvironmentKind.Test, result.Environment.Kind);
        Assert.All(result.Diagnostics, d => Assert.Equal(AnalysisSeverity.Error, d.Severity));
    }
    [Fact]
    public void Unknown_environment_still_reports_dangerous_statements()
    {
        var check = SqlSafetyCheck.Run("DROP TABLE dbo.T;", new("q.sql"), SqlEnvironmentConfiguration.Default);
        Assert.Equal(SqlEnvironmentKind.Unknown, check.Environment.Kind); Assert.Single(check.Diagnostics);
    }
    [Fact]
    public void Cancellation_never_becomes_a_successful_empty_check()
    {
        var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => new DangerousSqlAnalyzer().Analyze(Parse("SELECT 1;"), token));
        Assert.Throws<OperationCanceledException>(() => SqlSafetyCheck.Run("SELECT 1;", new("q.sql"), SqlEnvironmentConfiguration.Default, token));
    }
}
