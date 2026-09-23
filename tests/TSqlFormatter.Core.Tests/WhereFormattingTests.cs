using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class WhereFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select Id from Items where Id=1", "SELECT Id\nFROM Items\nWHERE\n    Id = 1")]
    [InlineData("select Id where Id>=@Min", "SELECT Id\nWHERE\n    Id >= @Min")]
    [InlineData("select Id from Items where Id!=2 and State='open'",
        "SELECT Id\nFROM Items\nWHERE\n    Id != 2\n    AND State = 'open'")]
    [InlineData("select Id from Items where Id=1 or Id=2",
        "SELECT Id\nFROM Items\nWHERE\n    Id = 1\n    OR Id = 2")]
    public void Formats_basic_comparisons_and_connectives(string source, string expected)
    {
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Unsupported_predicate_keeps_original_layout()
    {
        const string source = "select Id from Items where Id is null";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id FROM Items WHERE Id IS NULL", result.Text);
    }

    [Fact]
    public void Format_is_idempotent()
    {
        const string source = "select Id from Items where Id=1 and State='open'";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }
}
