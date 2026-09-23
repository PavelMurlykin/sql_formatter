using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class BinaryQueryFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select Id from A union select Id from B", "UNION")]
    [InlineData("select Id from A union all select Id from B", "UNION ALL")]
    [InlineData("select Id from A intersect select Id from B", "INTERSECT")]
    [InlineData("select Id from A except select Id from B", "EXCEPT")]
    public void Formats_set_operators(string source, string keyword)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM A\n" + keyword + "\nSELECT Id\nFROM B", first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Formats_chained_operators_and_final_order_by()
    {
        var result = _formatter.Format(
            "select Id from A union all select Id from B except select Id from C order by Id;",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM A\nUNION ALL\nSELECT Id\nFROM B\nEXCEPT\nSELECT Id\nFROM C\nORDER BY Id;", result.Text);
    }

    [Fact]
    public void Preserves_operator_case_when_requested()
    {
        var result = _formatter.Format("select Id from A union All select Id from B",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("select Id\nfrom A\nunion All\nselect Id\nfrom B", result.Text);
    }

    [Fact]
    public void Leaves_operator_comment_in_place()
    {
        const string source = "select Id from A /* keep */ union select Id from B";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id FROM A /* keep */ UNION SELECT Id FROM B", result.Text);
    }

    [Fact]
    public void Formats_set_operator_in_derived_table()
    {
        var result = _formatter.Format(
            "select d.Id from (select Id from A union all select Id from B) d",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT d.Id\nFROM (\n    SELECT Id\n    FROM A\n    UNION ALL\n    SELECT Id\n    FROM B\n) d", result.Text);
    }
}
