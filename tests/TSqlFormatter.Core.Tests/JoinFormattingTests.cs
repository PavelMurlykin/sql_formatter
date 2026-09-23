using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class JoinFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("inner join", "INNER JOIN")]
    [InlineData("left join", "LEFT JOIN")]
    [InlineData("right join", "RIGHT JOIN")]
    [InlineData("full join", "FULL JOIN")]
    [InlineData("left outer join", "LEFT OUTER JOIN")]
    [InlineData("join", "JOIN")]
    public void Formats_qualified_joins(string sourceJoin, string expectedJoin)
    {
        var source = $"select a.Id from dbo.A a {sourceJoin} dbo.B b on a.Id = b.Id";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal($"SELECT a.Id\nFROM dbo.A a\n{expectedJoin} dbo.B b\n    ON a.Id = b.Id", result.Text);
    }

    [Theory]
    [InlineData("cross join", "dbo.B b", "SELECT a.Id\nFROM dbo.A a\nCROSS JOIN dbo.B b")]
    [InlineData("cross apply", "(select b.Id from dbo.B b) x", "SELECT a.Id\nFROM dbo.A a\nCROSS APPLY (\n    SELECT b.Id\n    FROM dbo.B b\n) x")]
    [InlineData("outer apply", "(select b.Id from dbo.B b) x", "SELECT a.Id\nFROM dbo.A a\nOUTER APPLY (\n    SELECT b.Id\n    FROM dbo.B b\n) x")]
    public void Formats_unqualified_joins(string sourceJoin, string right, string expected)
    {
        var source = $"select a.Id from dbo.A a {sourceJoin} {right}";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Theory]
    [InlineData(KeywordCase.Lower, "select a.Id\nfrom A a\ncross apply (\n    select 1\n) x")]
    [InlineData(KeywordCase.Preserve, "SELECT a.Id\nFROM A a\nCross Apply (\n    SELECT 1\n) x")]
    public void Join_operator_respects_keyword_case(KeywordCase keywordCase, string expected)
    {
        var options = new FormattingOptions(keywords: new KeywordOptions(keywordCase));
        var result = _formatter.Format(
            "SELECT a.Id FROM A a Cross Apply (SELECT 1) x", options, new FormatRequest());

        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void Formats_join_chain_on_separate_lines()
    {
        const string source = "select a.Id from A a join B b on a.Id=b.Id left join C c on b.Id=c.Id;";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT a.Id\nFROM A a\nJOIN B b\n    ON a.Id = b.Id\nLEFT JOIN C c\n    ON b.Id = c.Id;", result.Text);
    }

    [Fact]
    public void Unsupported_join_hint_preserves_layout()
    {
        const string source = "select a.Id from A a inner hash join B b on a.Id=b.Id";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT a.Id FROM A a INNER hash JOIN B b ON a.Id=b.Id", result.Text);
    }

    [Fact]
    public void Formatting_join_twice_is_idempotent()
    {
        const string source = "select a.Id from A a left join B b on a.Id=b.Id;";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }
}
