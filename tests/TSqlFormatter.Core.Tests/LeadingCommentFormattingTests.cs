using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class LeadingCommentFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select Id from T\n-- filter\nwhere Id=1",
        "SELECT Id\nFROM T\n-- filter\nWHERE\n    Id = 1")]
    [InlineData("select Id\n-- source\nfrom T",
        "SELECT Id\n-- source\nFROM T")]
    [InlineData("select Id from T\n-- first\n-- second\nwhere Id=1",
        "SELECT Id\nFROM T\n-- first\n-- second\nWHERE\n    Id = 1")]
    [InlineData("select Id from T\n-- grouping\ngroup by Id\n-- sorting\norder by Id",
        "SELECT Id\nFROM T\n-- grouping\nGROUP BY Id\n-- sorting\nORDER BY Id")]
    public void Keeps_leading_comments_before_clauses(string source, string expected)
    {
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Keeps_comment_before_select()
    {
        var result = _formatter.Format("-- header\nselect Id from T", new FormattingOptions(), new FormatRequest());

        Assert.Equal("-- header\nSELECT Id\nFROM T", result.Text);
    }

    [Fact]
    public void Leading_comment_formatting_is_idempotent()
    {
        const string source = "select Id from T\n-- filter\nwhere Id=1";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }
}
