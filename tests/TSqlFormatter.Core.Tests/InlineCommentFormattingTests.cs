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
    public void Block_comment_after_comma_is_attached_to_column()
    {
        const string source = "select Id, /* note */ Name from T";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT\n    Id, /* note */\n    Name\nFROM T", result.Text);
    }

    [Theory]
    [InlineData("select Id,\n-- name\nName from T",
        "SELECT\n    Id,\n    -- name\n    Name\nFROM T")]
    [InlineData("select Id, -- separator\n-- name\nName from T",
        "SELECT\n    Id, -- separator\n    -- name\n    Name\nFROM T")]
    [InlineData("select Id,\n-- first\n-- second\nName from T",
        "SELECT\n    Id,\n    -- first\n    -- second\n    Name\nFROM T")]
    [InlineData("select Id, /* first */\n-- second\nName from T",
        "SELECT\n    Id, /* first */\n    -- second\n    Name\nFROM T")]
    public void Keeps_adjacent_comment_chains_with_the_next_column(string source, string expected)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
        Assert.Equal(Count(source, "--"), Count(first.Text, "--"));
    }

    [Fact]
    public void Detached_comment_in_column_separator_keeps_source_layout()
    {
        const string source = "select Id,\n\n-- detached\nName from T";

        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id,\n\n-- detached\nName FROM T", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Blank_line_inside_comment_chain_keeps_source_layout()
    {
        const string source = "select Id, -- first\n\n-- detached\nName from T";

        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id, -- first\n\n-- detached\nName FROM T", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Ambiguous_same_line_block_comment_chain_keeps_source_layout()
    {
        const string source = "select Id, /* first */ /* second */ Name from T";

        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id, /* first */ /* second */ Name FROM T", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    private static int Count(string source, string fragment) =>
        source.Split(new[] { fragment }, StringSplitOptions.None).Length - 1;
}
