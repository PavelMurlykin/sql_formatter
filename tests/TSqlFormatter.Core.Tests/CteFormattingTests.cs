using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class CteFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Fact]
    public void Formats_single_cte()
    {
        var result = _formatter.Format(
            "with X as (select Id from T) select Id from X",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("WITH X AS (\n    SELECT Id\n    FROM T\n)\nSELECT Id\nFROM X", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Formats_multiple_ctes_and_column_aliases()
    {
        var result = _formatter.Format(
            "with A(Id) as (select Id from T),B as (select Id from A) select Id from B;",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("WITH A (Id) AS (\n    SELECT Id\n    FROM T\n),\nB AS (\n    SELECT Id\n    FROM A\n)\nSELECT Id\nFROM B;", result.Text);
    }

    [Fact]
    public void Formatting_cte_twice_is_idempotent()
    {
        const string source = "with X as (select Id from T) select Id from X";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }

    [Theory]
    [InlineData("with X as (select Id from T) delete from X where Id=1;",
        "WITH X AS (\n    SELECT Id\n    FROM T\n)\nDELETE FROM X\nWHERE\n    Id = 1;")]
    [InlineData("with X as (select Id from T) update X set Id=2 where Id=1;",
        "WITH X AS (\n    SELECT Id\n    FROM T\n)\nUPDATE X\nSET\n    Id = 2\nWHERE\n    Id = 1;")]
    [InlineData("with X as (select Id from T) insert into U(Id) select Id from X;",
        "WITH X AS (\n    SELECT Id\n    FROM T\n)\nINSERT INTO U (Id)\nSELECT Id\nFROM X;")]
    public void Formats_cte_before_dml(string source, string expected)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.Equal(expected, first.Text);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Multiple_ctes_before_delete_keep_literal_and_reparse()
    {
        const string source = "with A as (select Id from T where Note='a  b'), B as (select Id from A) delete from B where Id=1;";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.Contains("'a  b'", first.Text);
        Assert.Contains("DELETE FROM B\nWHERE", first.Text);
        Assert.Equal(first.Text, _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest()).Text);
    }

    [Fact]
    public void Comment_between_cte_and_dml_preserves_original_layout()
    {
        const string source = "with X as (select Id from T) /* keep */ delete from X where Id=1;";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)), new FormatRequest());
        Assert.Equal(source, result.Text);
    }
}
