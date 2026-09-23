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
}
