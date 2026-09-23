using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class SubqueryFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Fact]
    public void Formats_scalar_subquery_in_select_list()
    {
        var result = _formatter.Format(
            "select (select max(Id) from T) as MaxId from U",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT\n    (\n        SELECT max(Id)\n        FROM T\n    ) AS MaxId\nFROM U", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Theory]
    [InlineData("exists (select 1 from U where U.Id=T.Id)",
        "EXISTS (\n        SELECT 1\n        FROM U\n        WHERE\n            U.Id = T.Id\n    )")]
    [InlineData("Id in (select Id from U)",
        "Id IN (\n        SELECT Id\n        FROM U\n    )")]
    [InlineData("Id not in (select Id from U)",
        "Id NOT IN (\n        SELECT Id\n        FROM U\n    )")]
    public void Formats_exists_and_in_subqueries(string predicate, string expectedPredicate)
    {
        var result = _formatter.Format(
            "select Id from T where " + predicate,
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM T\nWHERE\n    " + expectedPredicate, result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Formats_scalar_subquery_in_comparison()
    {
        var result = _formatter.Format(
            "select Id from T where Id=(select max(Id) from U)",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM T\nWHERE\n    Id = (\n        SELECT max(Id)\n        FROM U\n    )", result.Text);
    }

    [Fact]
    public void Derived_subquery_remains_supported()
    {
        var result = _formatter.Format(
            "select d.Id from (select Id from T) d",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT d.Id\nFROM (\n    SELECT Id\n    FROM T\n) d", result.Text);
    }

    [Fact]
    public void Subquery_formatting_is_idempotent()
    {
        const string source = "select Id from T where exists (select 1 from U where U.Id=T.Id)";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }

    [Theory]
    [InlineData("where Id=1 and exists (select 1 from U)",
        "WHERE\n    Id = 1\n    AND EXISTS (\n        SELECT 1\n        FROM U\n    )")]
    [InlineData("where exists (select 1 from U) and Id=1",
        "WHERE\n    EXISTS (\n        SELECT 1\n        FROM U\n    )\n    AND Id = 1")]
    [InlineData("where (exists (select 1 from U))",
        "WHERE\n    (\n        EXISTS (\n            SELECT 1\n            FROM U\n        )\n    )")]
    public void Formats_exists_in_logical_groups(string condition, string expected)
    {
        var result = _formatter.Format("select Id from T " + condition,
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM T\n" + expected, result.Text);
    }

    [Fact]
    public void Preserves_keyword_case_in_exists_subquery()
    {
        var result = _formatter.Format("select Id from T where exists (select 1 from U)",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("select Id\nfrom T\nwhere\n    exists (\n        select 1\n        from U\n    )", result.Text);
    }

    [Fact]
    public void Formats_exists_subquery_in_join_condition()
    {
        var result = _formatter.Format(
            "select a.Id from A a join B b on exists (select 1 from U where U.Id=b.Id)",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT a.Id\nFROM A a\nJOIN B b\n    ON EXISTS (\n        SELECT 1\n        FROM U\n        WHERE\n            U.Id = b.Id\n    )", result.Text);
    }

}
