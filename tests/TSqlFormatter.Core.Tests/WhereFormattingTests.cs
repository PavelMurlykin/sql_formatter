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
    public void Formats_is_null_predicate()
    {
        const string source = "select Id from Items where Id is null";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM Items\nWHERE\n    Id IS NULL", result.Text);
    }

    [Theory]
    [InlineData("select Id from Items where Id is not null and Name like 'A%'",
        "SELECT Id\nFROM Items\nWHERE\n    Id IS NOT NULL\n    AND Name LIKE 'A%'")]
    [InlineData("select Id from Items where Name not like '%x' or Id=2",
        "SELECT Id\nFROM Items\nWHERE\n    Name NOT LIKE '%x'\n    OR Id = 2")]
    public void Formats_null_and_like_with_boolean_connectives(string source, string expected)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        Assert.Equal(expected, first.Text);
        Assert.Equal(first.Text, _formatter.Format(first.Text,
            new FormattingOptions(), new FormatRequest()).Text);
    }

    [Fact]
    public void Leaves_escape_like_and_adjacent_comment_untouched()
    {
        const string source = "select Id from Items where Name like 'A%' escape '!' /* keep */ and Id=2";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());
        Assert.Equal(source, result.Text);
    }

    [Fact]
    public void Nested_predicates_preserve_literal_and_reparse()
    {
        const string source = "select Id from Items where (Name like 'a  %' or Id is null) and Flag=1";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.Contains("'a  %'", first.Text);
        Assert.Contains("Name LIKE", first.Text);
        Assert.Equal(first.Text, _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest()).Text);
    }

    [Fact]
    public void Where_line_break_options_keep_condition_and_connective_inline()
    {
        const string source = "select Id from Items where Id=1 and State='open'";
        var options = FormattingOptions.Default.With(where: new WhereOptions(false, false));

        var result = _formatter.Format(source, options, new FormatRequest());
        var repeated = _formatter.Format(result.Text, options, new FormatRequest());

        Assert.Equal("SELECT Id\nFROM Items\nWHERE Id = 1 AND State = 'open'", result.Text);
        Assert.False(repeated.Changed);
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
