using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Analysis;
using TSqlFormatter.Core.Analysis.Rules;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlAnalyzerTests
{
    private readonly ISqlParser _parser = new ScriptDomSqlParser();
    private readonly SqlAnalyzer _analyzer = new SqlAnalyzer();

    private AnalysisResult Analyze(string sql, AnalysisOptions? options = null)
    {
        var parseResult = _parser.Parse(sql, SqlDialectVersion.Auto);
        return _analyzer.Analyze(parseResult, options);
    }

    [Fact]
    public void DefaultRules_ContainFiveUniqueCodes()
    {
        var codes = SqlAnalyzer.DefaultRules.Select(rule => rule.Code).ToArray();

        Assert.Equal(
            new[] { "TSQL001", "TSQL002", "TSQL003", "TSQL004", "TSQL005" },
            codes);
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SelectStar_ProducesWarningAtStar()
    {
        const string sql = "SELECT * FROM dbo.Orders;";

        var result = Analyze(sql);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(SelectStarRule.RuleCode, diagnostic.Code);
        Assert.Equal(AnalysisSeverity.Warning, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
        Assert.Equal(7, diagnostic.Span.StartOffset);
        Assert.Equal(1, diagnostic.Span.Length);
        Assert.InRange(diagnostic.Span.EndOffset, 0, sql.Length);
    }

    [Fact]
    public void SelectStar_DetectsQualifiedStar()
    {
        var result = Analyze("SELECT o.* FROM dbo.Orders AS o;");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(SelectStarRule.RuleCode, diagnostic.Code);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
    }

    [Fact]
    public void SelectStar_DetectsStarInSubquery()
    {
        var result = Analyze("SELECT Id FROM dbo.Orders WHERE CustomerId IN (SELECT * FROM dbo.Customers);");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(SelectStarRule.RuleCode, diagnostic.Code);
    }

    [Fact]
    public void SelectStar_DetectsStarsInDerivedTableAndOuterQuery()
    {
        var result = Analyze("SELECT s.* FROM (SELECT * FROM dbo.Orders) AS s;");

        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(SelectStarRule.RuleCode, diagnostic.Code));
    }

    [Fact]
    public void CountStar_IsNotReported()
    {
        var result = Analyze("SELECT COUNT(*) FROM dbo.Orders;");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExplicitColumnList_IsNotReported()
    {
        var result = Analyze("SELECT Id, Name FROM dbo.Orders;");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void InsertWithoutColumnList_ReportsValues()
    {
        var result = Analyze("INSERT INTO dbo.Orders VALUES (1, 2);");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(InsertWithoutColumnListRule.RuleCode, diagnostic.Code);
        Assert.Equal(AnalysisSeverity.Warning, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }

    [Fact]
    public void InsertWithoutColumnList_ReportsInsertSelect()
    {
        var result = Analyze("INSERT INTO dbo.Orders SELECT Id, Name FROM dbo.Stage;");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(InsertWithoutColumnListRule.RuleCode, diagnostic.Code);
    }

    [Fact]
    public void InsertWithColumnList_IsNotReported()
    {
        var result = Analyze("INSERT INTO dbo.Orders (Id, Name) VALUES (1, 2);");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void InsertDefaultValues_IsNotReported()
    {
        var result = Analyze("INSERT INTO dbo.Orders DEFAULT VALUES;");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TopWithoutOrderBy_IsReported()
    {
        var result = Analyze("SELECT TOP (5) Id FROM dbo.Orders;");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(TopWithoutOrderByRule.RuleCode, diagnostic.Code);
        Assert.Equal(AnalysisSeverity.Warning, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
    }

    [Fact]
    public void TopWithOrderBy_IsNotReported()
    {
        var result = Analyze("SELECT TOP (5) Id FROM dbo.Orders ORDER BY Id;");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TopInCteWithOwnOrderBy_IsNotReported()
    {
        var result = Analyze("WITH c AS (SELECT TOP (5) Id FROM dbo.Orders ORDER BY Id) SELECT Id FROM c;");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TopInUnionBranchWithoutOwnOrderBy_IsReported()
    {
        var result = Analyze("SELECT TOP (1) Id FROM dbo.Orders UNION SELECT Id FROM dbo.Customers;");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(TopWithoutOrderByRule.RuleCode, diagnostic.Code);
    }

    [Fact]
    public void UpdateWithoutWhere_IsReportedAsError()
    {
        const string sql = "UPDATE dbo.Orders SET Status = 1;";

        var result = Analyze(sql);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(UpdateWithoutWhereRule.RuleCode, diagnostic.Code);
        Assert.Equal(AnalysisSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
        Assert.Equal(0, diagnostic.Span.StartOffset);
    }

    [Fact]
    public void UpdateWithWhere_IsNotReported()
    {
        var result = Analyze("UPDATE dbo.Orders SET Status = 1 WHERE Id = 1;");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DeleteWithoutWhere_IsReportedAsError()
    {
        var result = Analyze("DELETE FROM dbo.Orders;");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DeleteWithoutWhereRule.RuleCode, diagnostic.Code);
        Assert.Equal(AnalysisSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }

    [Fact]
    public void DeleteWithWhere_IsNotReported()
    {
        var result = Analyze("DELETE FROM dbo.Orders WHERE Id = 1;");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void MultipleFindings_AreSortedByOffsetAndCarryLineNumbers()
    {
        const string sql = "SELECT * FROM dbo.Orders;\nUPDATE dbo.Orders SET Status = 1;\nDELETE FROM dbo.Orders;";

        var result = Analyze(sql);

        Assert.Equal(3, result.Diagnostics.Count);
        Assert.Equal(SelectStarRule.RuleCode, result.Diagnostics[0].Code);
        Assert.Equal(1, result.Diagnostics[0].Line);
        Assert.Equal(UpdateWithoutWhereRule.RuleCode, result.Diagnostics[1].Code);
        Assert.Equal(2, result.Diagnostics[1].Line);
        Assert.Equal(1, result.Diagnostics[1].Column);
        Assert.Equal(DeleteWithoutWhereRule.RuleCode, result.Diagnostics[2].Code);
        Assert.Equal(3, result.Diagnostics[2].Line);
        Assert.True(result.HasDiagnostics);
    }

    [Fact]
    public void DisabledRule_IsSkipped()
    {
        var options = new AnalysisOptions();
        options.DisabledRules.Add(SelectStarRule.RuleCode);

        var result = Analyze("SELECT * FROM dbo.Orders;", options);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SeverityOverride_AppliesOverRuleDefault()
    {
        var options = new AnalysisOptions();
        options.SeverityOverrides[SelectStarRule.RuleCode] = AnalysisSeverity.Info;

        var result = Analyze("SELECT * FROM dbo.Orders;", options);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(AnalysisSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public void DisabledRules_AndOverrides_MatchCodesCaseInsensitively()
    {
        var options = new AnalysisOptions();
        options.DisabledRules.Add("tsql004");
        options.SeverityOverrides["tsql005"] = AnalysisSeverity.Warning;

        var result = Analyze("UPDATE dbo.Orders SET Status = 1;\nDELETE FROM dbo.Orders;", options);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DeleteWithoutWhereRule.RuleCode, diagnostic.Code);
        Assert.Equal(AnalysisSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void NullRoot_ReturnsEmptyResult()
    {
        var parseResult = new SqlParseResult(
            "SELECT 1",
            SqlDialectVersion.Auto,
            SqlVersion.Sql180,
            root: null,
            Array.Empty<TSqlParserToken>(),
            Array.Empty<ParseDiagnostic>());

        var result = _analyzer.Analyze(parseResult);

        Assert.False(result.HasDiagnostics);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CanceledToken_ThrowsOperationCanceled()
    {
        var parseResult = _parser.Parse("SELECT * FROM dbo.Orders;", SqlDialectVersion.Auto);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => _analyzer.Analyze(parseResult, cancellationToken: cancellationSource.Token));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlAnalyzer(rules: null!));
        Assert.Throws<ArgumentException>(() => new SqlAnalyzer(new IAnalysisRule[] { null! }));
        Assert.Throws<ArgumentNullException>(() => _analyzer.Analyze(parseResult: null!));

        var parseResult = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        Assert.Throws<ArgumentNullException>(() => new TopWithoutOrderByRule().Analyze(root: null!));
    }
}
