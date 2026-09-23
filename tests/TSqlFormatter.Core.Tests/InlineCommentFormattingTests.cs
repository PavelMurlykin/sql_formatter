using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class InlineCommentFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select Id, -- comment\nName from T",
        "SELECT\n    Id, -- comment\n    Name\nFROM T")]
    [InlineData("select Id,-- first\r\nName, -- second\r\nValue from T;",
        "SELECT\n    Id, -- first\n    Name, -- second\n    Value\nFROM T;")]
    public void Keeps_inline_comments_attached_to_select_columns(string source, string expected)
    {
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, result.Text);
        Assert.Equal(Count(source, "--"), Count(result.Text, "--"));
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Inline_comment_formatting_is_idempotent()
    {
        const string source = "select Id, -- note\nName from T";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }

    [Fact]
    public void Leading_comment_still_uses_source_preserving_fallback()
    {
        const string source = "select -- note\n Id, Name from T";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT -- note\n Id, Name FROM T", result.Text);
    }

    [Fact]
    public void Generated_inline_comment_lines_use_selected_line_ending()
    {
        var options = new FormattingOptions(general: new GeneralOptions(lineEnding: DocLineEnding.CrLf));
        var result = _formatter.Format("select Id, -- note\nName from T", options, new FormatRequest());

        Assert.Equal("SELECT\r\n    Id, -- note\r\n    Name\r\nFROM T", result.Text);
    }

    [Fact]
    public void Unsupported_block_comment_is_not_dropped()
    {
        const string source = "select Id, /* note */ Name from T";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id, /* note */ Name FROM T", result.Text);
    }

    private static int Count(string source, string fragment) =>
        source.Split(new[] { fragment }, StringSplitOptions.None).Length - 1;
}
