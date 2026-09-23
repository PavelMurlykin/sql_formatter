using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class FromFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select t.Id from Sales.Orders t", "SELECT t.Id\nFROM Sales.Orders t")]
    [InlineData("select t.Id from [Sales].[Orders] as t;", "SELECT t.Id\nFROM [Sales].[Orders] AS t;")]
    public void Keeps_qualified_names_and_aliases(string source, string expected)
    {
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Formats_basic_derived_table()
    {
        var result = _formatter.Format(
            "select d.Id from (select Id from dbo.Items) as d;",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT d.Id\nFROM (\n    SELECT Id\n    FROM dbo.Items\n) AS d;", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Preserves_unsupported_derived_column_alias_list()
    {
        const string source = "select d.Value from (select 1) as d(Value)";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT d.Value FROM (SELECT 1) AS d(Value)", result.Text);
    }
}
