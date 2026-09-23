using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class WindowFunctionFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select row_number() over(partition by Category, Region order by CreatedAt desc) as rn from T",
        "SELECT\n    row_number() OVER (\n        PARTITION BY Category, Region\n        ORDER BY CreatedAt DESC\n    ) AS rn\nFROM T")]
    [InlineData("select sum(Amount) over(partition by Category) as Total from T",
        "SELECT\n    sum(Amount) OVER (\n        PARTITION BY Category\n    ) AS Total\nFROM T")]
    [InlineData("select row_number() over(order by Id) as rn from T",
        "SELECT\n    row_number() OVER (\n        ORDER BY Id\n    ) AS rn\nFROM T")]
    [InlineData("select count(*) over() as Total from T",
        "SELECT count(*) OVER () AS Total\nFROM T")]
    public void Formats_over_partition_and_order(string source, string expected)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Preserves_keyword_spelling_when_requested()
    {
        var result = _formatter.Format("select sum(Amount) over(partition by Category) from T",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("select\n    sum(Amount) over (\n        partition by Category\n    )\nfrom T", result.Text);
    }

    [Fact]
    public void Leaves_window_frame_in_original_layout()
    {
        const string source = "select sum(Amount) over(order by Id rows between unbounded preceding and current row) from T";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal(source, result.Text);
    }
}
