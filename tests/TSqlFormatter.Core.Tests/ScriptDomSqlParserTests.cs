using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class ScriptDomSqlParserTests
{
    private readonly ISqlParser _parser = new ScriptDomSqlParser();

    [Fact]
    public void Parse_ValidSelect_ReturnsAstTokensAndOriginalSource()
    {
        const string source = "SELECT 1;";

        var result = _parser.Parse(source, SqlDialectVersion.Auto);

        Assert.True(result.ParseSucceeded);
        Assert.Equal(source, result.Source);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(SqlDialectVersion.Auto, result.RequestedDialect);
        Assert.Equal(SqlVersion.Sql180, result.ParserVersion);
        var script = Assert.IsType<TSqlScript>(result.Root);
        Assert.IsType<SelectStatement>(Assert.Single(Assert.Single(script.Batches).Statements));
        Assert.Contains(result.Tokens, token => token.Text == "SELECT");
    }

    [Fact]
    public void Parse_InvalidSelect_ReturnsPositionedDiagnosticAndPreservesSource()
    {
        const string source = "SELECT FROM;";

        var result = _parser.Parse(source, SqlDialectVersion.Sql2022);

        Assert.False(result.ParseSucceeded);
        Assert.Equal(source, result.Source);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.True(diagnostic.ParserErrorNumber > 0);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        Assert.InRange(diagnostic.Offset, 0, source.Length);
        Assert.Equal(1, diagnostic.Line);
        Assert.NotEmpty(result.Tokens);
    }

    [Fact]
    public void Parse_MultipleStatements_ReturnsAllStatements()
    {
        var result = _parser.Parse("SELECT 1; SELECT 2;", SqlDialectVersion.Sql2019);

        Assert.True(result.ParseSucceeded);
        var script = Assert.IsType<TSqlScript>(result.Root);
        Assert.Equal(2, Assert.Single(script.Batches).Statements.Count);
    }

    [Fact]
    public void Parse_Comments_PreservesCommentTokens()
    {
        const string source = "-- heading\nSELECT 1; /* trailing */";

        var result = _parser.Parse(source, SqlDialectVersion.Auto);

        Assert.True(result.ParseSucceeded);
        Assert.Equal(source, result.Source);
        Assert.Contains(result.Tokens, token => token.Text == "-- heading");
        Assert.Contains(result.Tokens, token => token.Text == "/* trailing */");
    }

    [Fact]
    public void Parse_QuotedIdentifiers_PreservesTokenText()
    {
        const string source = "SELECT [from], \"Order\" FROM [dbo].[items];";

        var result = _parser.Parse(source, SqlDialectVersion.Sql2022);

        Assert.True(result.ParseSucceeded);
        Assert.Contains(result.Tokens, token => token.Text == "[from]");
        Assert.Contains(result.Tokens, token => token.Text == "\"Order\"");
    }

    [Fact]
    public void Parse_StringLiteral_PreservesEscapesAndCommentMarkers()
    {
        const string source = "SELECT 'it''s -- text /* not comment */';";

        var result = _parser.Parse(source, SqlDialectVersion.Auto);

        Assert.True(result.ParseSucceeded);
        Assert.Contains(result.Tokens, token => token.Text == "'it''s -- text /* not comment */'");
    }

    [Theory]
    [InlineData(SqlDialectVersion.Auto, SqlVersion.Sql180)]
    [InlineData(SqlDialectVersion.Sql2016, SqlVersion.Sql130)]
    [InlineData(SqlDialectVersion.Sql2017, SqlVersion.Sql140)]
    [InlineData(SqlDialectVersion.Sql2019, SqlVersion.Sql150)]
    [InlineData(SqlDialectVersion.Sql2022, SqlVersion.Sql160)]
    [InlineData(SqlDialectVersion.Latest, SqlVersion.Sql180)]
    public void Parse_Dialect_UsesExpectedScriptDomVersion(
        SqlDialectVersion dialect,
        SqlVersion expectedVersion)
    {
        var result = _parser.Parse("SELECT 1;", dialect);

        Assert.Equal(expectedVersion, result.ParserVersion);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Parse_CanceledRequest_ThrowsBeforeParsing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _parser.Parse("SELECT 1;", SqlDialectVersion.Auto, cancellation.Token));
    }
}
