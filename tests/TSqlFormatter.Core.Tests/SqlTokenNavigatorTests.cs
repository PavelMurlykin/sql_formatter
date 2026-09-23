using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlTokenNavigatorTests
{
    private readonly ISqlParser _parser = new ScriptDomSqlParser();

    [Fact]
    public void GetToken_ReturnsTokenAtIndexAndRejectsOutOfRangeIndices()
    {
        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        var navigator = new SqlTokenNavigator(result);

        Assert.Same(result.Tokens[0], navigator.GetToken(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => navigator.GetToken(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => navigator.GetToken(result.Tokens.Count));
    }

    [Fact]
    public void MeaningfulNeighbors_SkipWhitespaceCommentsAndEndOfFile()
    {
        const string source = "-- header\r\nSELECT /* inline */ \r\n 1; -- tail";
        var result = _parser.Parse(source, SqlDialectVersion.Auto);
        var navigator = new SqlTokenNavigator(result);
        var selectIndex = IndexOfToken(result, "SELECT");
        var numberIndex = IndexOfToken(result, "1");
        var semicolonIndex = IndexOfToken(result, ";");
        var headerIndex = IndexOfToken(result, "-- header");

        Assert.Null(navigator.GetPreviousMeaningfulToken(headerIndex));
        Assert.Null(navigator.GetPreviousMeaningfulToken(selectIndex));
        Assert.Equal("SELECT", navigator.GetNextMeaningfulToken(headerIndex)?.Text);
        Assert.Equal("1", navigator.GetNextMeaningfulToken(selectIndex)?.Text);
        Assert.Equal("SELECT", navigator.GetPreviousMeaningfulToken(numberIndex)?.Text);
        Assert.Equal(";", navigator.GetPreviousMeaningfulToken(result.Tokens.Count - 1)?.Text);
        Assert.Null(navigator.GetNextMeaningfulToken(semicolonIndex));
    }

    [Fact]
    public void MeaningfulNeighbors_RejectInvalidIndices()
    {
        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        var navigator = new SqlTokenNavigator(result);

        Assert.Throws<ArgumentOutOfRangeException>(() => navigator.GetPreviousMeaningfulToken(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => navigator.GetNextMeaningfulToken(result.Tokens.Count));
    }

    [Fact]
    public void GetFragmentTokens_IncludesBothBoundsAndInteriorTrivia()
    {
        var result = _parser.Parse("SELECT /* note */ 1;", SqlDialectVersion.Auto);
        var script = Assert.IsType<TSqlScript>(result.Root);
        var statement = Assert.IsType<SelectStatement>(Assert.Single(Assert.Single(script.Batches).Statements));
        var navigator = new SqlTokenNavigator(result);

        var tokens = navigator.GetFragmentTokens(statement);

        Assert.Same(result.Tokens[statement.FirstTokenIndex], tokens[0]);
        Assert.Same(result.Tokens[statement.LastTokenIndex], tokens[^1]);
        Assert.Equal(statement.LastTokenIndex - statement.FirstTokenIndex + 1, tokens.Count);
        Assert.Contains(tokens, token => token.Text == "/* note */");
        Assert.Contains(tokens, token => token.TokenType == TSqlTokenType.WhiteSpace);
    }

    [Fact]
    public void GetTextSpan_UsesFragmentOffsetsInOriginalSource()
    {
        const string source = "  SELECT /* note */ 1;\r\n";
        var result = _parser.Parse(source, SqlDialectVersion.Auto);
        var script = Assert.IsType<TSqlScript>(result.Root);
        var statement = Assert.IsType<SelectStatement>(Assert.Single(Assert.Single(script.Batches).Statements));
        var navigator = new SqlTokenNavigator(result);

        var span = navigator.GetTextSpan(statement);

        Assert.Equal(statement.StartOffset, span.StartOffset);
        Assert.Equal(statement.FragmentLength, span.Length);
        Assert.Equal(span.StartOffset + span.Length, span.EndOffset);
        Assert.Equal("SELECT /* note */ 1;", source.Substring(span.StartOffset, span.Length));
    }

    [Fact]
    public void Helpers_RejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlTokenNavigator(null!));

        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        var navigator = new SqlTokenNavigator(result);
        Assert.Throws<ArgumentNullException>(() => navigator.GetFragmentTokens(null!));
        Assert.Throws<ArgumentNullException>(() => navigator.GetTextSpan(null!));
    }

    [Fact]
    public void TokenNavigation_RemainsAvailableWhenParsingReportsErrors()
    {
        var result = _parser.Parse("SELECT FROM;", SqlDialectVersion.Auto);
        var navigator = new SqlTokenNavigator(result);

        Assert.False(result.ParseSucceeded);
        Assert.Equal("SELECT", navigator.GetToken(IndexOfToken(result, "SELECT")).Text);
        Assert.Equal("FROM", navigator.GetNextMeaningfulToken(IndexOfToken(result, "SELECT"))?.Text);
    }

    [Fact]
    public void EmptySource_HasNoMeaningfulNeighbors()
    {
        var result = _parser.Parse(string.Empty, SqlDialectVersion.Auto);
        var navigator = new SqlTokenNavigator(result);
        var script = Assert.IsType<TSqlScript>(result.Root);

        for (var index = 0; index < result.Tokens.Count; index++)
        {
            Assert.Null(navigator.GetPreviousMeaningfulToken(index));
            Assert.Null(navigator.GetNextMeaningfulToken(index));
        }

        var span = navigator.GetTextSpan(script);
        Assert.Equal(0, span.StartOffset);
        Assert.Equal(0, span.Length);
    }

    [Fact]
    public void FragmentHelpers_RejectUninitializedFragment()
    {
        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        var navigator = new SqlTokenNavigator(result);
        var fragment = new TSqlScript();

        Assert.Throws<ArgumentException>(() => navigator.GetFragmentTokens(fragment));
        Assert.Throws<ArgumentException>(() => navigator.GetTextSpan(fragment));
    }

    [Fact]
    public void SqlTextSpan_RejectsInvalidRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlTextSpan(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlTextSpan(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlTextSpan(int.MaxValue, 1));
    }

    private static int IndexOfToken(SqlParseResult result, string text)
    {
        for (var index = 0; index < result.Tokens.Count; index++)
        {
            if (result.Tokens[index].Text == text)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"Token '{text}' was not found.");
    }
}
