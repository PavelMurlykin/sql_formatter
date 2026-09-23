using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class ScriptDomSqlFormatterKeywordTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select 1", "SELECT 1")]
    [InlineData("Select 1", "SELECT 1")]
    [InlineData("SELECT 1", "SELECT 1")]
    [InlineData("select 'select' -- select\nfrom [select]", "SELECT 'select' -- select\nFROM [select]")]
    [InlineData("select /* from */ 1", "SELECT /* from */ 1")]
    public void Uppercases_only_keyword_tokens(string source, string expected)
    {
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.True(result.ParseSucceeded);
        Assert.Equal(expected, result.Text);
        Assert.Equal(source != expected, result.Changed);
    }

    [Fact]
    public void Lower_and_preserve_modes_are_supported()
    {
        const string source = "SELECT 'FROM' FROM dbo.TableName";
        var lower = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Lower)), new FormatRequest());
        var preserve = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)), new FormatRequest());

        Assert.Equal("select 'FROM'\nfrom dbo.TableName", lower.Text);
        Assert.Equal("SELECT 'FROM'\nFROM dbo.TableName", preserve.Text);
        Assert.True(preserve.Changed);
    }

    [Fact]
    public void Parse_error_leaves_source_untouched()
    {
        const string source = "select from";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.False(result.ParseSucceeded);
        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF1000");
    }

    [Fact]
    public void Unsupported_scope_leaves_source_untouched()
    {
        var result = _formatter.Format("select 1", new FormattingOptions(),
            new FormatRequest(FormatScope.Statement));

        Assert.Equal("select 1", result.Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF3000");
    }
}
