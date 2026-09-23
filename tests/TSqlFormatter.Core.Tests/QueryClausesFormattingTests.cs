using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class QueryClausesFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Fact]
    public void Formats_group_having_and_order()
    {
        const string source = "select Category,count(*) as Total from Sales where Active=1 group by Category having count(*)>1 order by Total desc;";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Category, count(*) AS Total\nFROM Sales\nWHERE\n    Active = 1\nGROUP BY Category\nHAVING\n    count(*) > 1\nORDER BY Total DESC;", result.Text);
    }

    [Fact]
    public void One_per_line_lists_keep_trailing_commas_and_sort_order()
    {
        var options = new FormattingOptions(clauses: new QueryClauseOptions(
            ClauseItemLayout.OnePerLine, ClauseItemLayout.OnePerLine));
        var result = _formatter.Format(
            "select A,B from T group by A,B order by A desc,B asc", options, new FormatRequest());

        Assert.Equal("SELECT A, B\nFROM T\nGROUP BY\n    A,\n    B\nORDER BY\n    A DESC,\n    B ASC", result.Text);
    }

    [Theory]
    [InlineData(80, "SELECT Alpha, Beta\nFROM T\nGROUP BY Alpha, Beta\nORDER BY Alpha DESC, Beta ASC")]
    [InlineData(16, "SELECT\n    Alpha,\n    Beta\nFROM T\nGROUP BY\n    Alpha,\n    Beta\nORDER BY\n    Alpha DESC,\n    Beta ASC")]
    public void Auto_wraps_group_and_order_lists(int width, string expected)
    {
        var options = new FormattingOptions(general: new GeneralOptions(maxLineWidth: width));
        var result = _formatter.Format(
            "select Alpha,Beta from T group by Alpha,Beta order by Alpha desc,Beta asc",
            options, new FormatRequest());

        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void Formats_twice_identically()
    {
        const string source = "select A from T group by A having count(*)>1 order by A desc;";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }

    [Fact]
    public void Rejects_unknown_clause_layout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QueryClauseOptions((ClauseItemLayout)99));
    }
}
