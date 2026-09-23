using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class BlockCommentFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select Id, /* label */ Name from T",
        "SELECT\n    Id, /* label */\n    Name\nFROM T")]
    [InlineData("select Id, /* first */ Name, /* second */ Age from T",
        "SELECT\n    Id, /* first */\n    Name, /* second */\n    Age\nFROM T")]
    [InlineData("select Id from T\n/* filter */\nwhere Id=1",
        "SELECT Id\nFROM T\n/* filter */\nWHERE\n    Id = 1")]
    public void Keeps_block_comments_while_formatting(string source, string expected)
    {
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, result.Text);
        Assert.Equal(Count(source, "/*"), Count(result.Text, "/*"));
        Assert.Equal(Count(source, "*/"), Count(result.Text, "*/"));
    }

    [Fact]
    public void Multiline_block_comment_is_not_lost()
    {
        const string source = "select Id, /* first\n second */ Name from T";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Contains("/* first\n second */", result.Text);
        Assert.Equal(1, Count(result.Text, "/*"));
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Block_comment_formatting_is_idempotent()
    {
        const string source = "select Id, /* note */ Name from T";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }

    private static int Count(string source, string fragment) =>
        source.Split(new[] { fragment }, StringSplitOptions.None).Length - 1;
}
