using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class BasicSelectFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select    1", "SELECT 1")]
    [InlineData("select  Name", "SELECT Name")]
    [InlineData("select Id,Name", "SELECT Id, Name")]
    [InlineData("select u.Id as UserId from dbo.Users u;", "SELECT u.Id AS UserId\nFROM dbo.Users u;")]
    [InlineData("select 1 from Items", "SELECT 1\nFROM Items")]
    public void Formats_basic_select(string source, string expected)
    {
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Basic_where_is_formatted()
    {
        const string source = "select  Id  from Items where Id = 1";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM Items\nWHERE\n    Id = 1", result.Text);
    }

    [Fact]
    public void Comments_in_select_are_preserved()
    {
        const string source = "select /* column */ Id from Items";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT /* column */ Id FROM Items", result.Text);
    }
}
