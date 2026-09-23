using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class Phase4SelectIntegrationTests
{
    [Fact]
    public void Complete_select_shape_matches_phase_exit_and_is_idempotent()
    {
        const string source = "select c.Id,c.Name,sum(o.Amount) as TotalAmount from dbo.Customer as c inner join dbo.[Order] as o on o.CustomerId=c.Id where c.IsActive=1 and o.CreatedAt>=@DateFrom group by c.Id,c.Name having sum(o.Amount)>0 order by TotalAmount desc;";
        const string expected = "SELECT\n    c.Id,\n    c.Name,\n    sum(o.Amount) AS TotalAmount\nFROM dbo.Customer AS c\nINNER JOIN dbo.[Order] AS o\n    ON o.CustomerId = c.Id\nWHERE\n    c.IsActive = 1\n    AND o.CreatedAt >= @DateFrom\nGROUP BY\n    c.Id,\n    c.Name\nHAVING\n    sum(o.Amount) > 0\nORDER BY\n    TotalAmount DESC;";
        var options = new FormattingOptions(
            select: new SelectOptions(SelectColumnLayout.OnePerLine),
            clauses: new QueryClauseOptions(ClauseItemLayout.OnePerLine, ClauseItemLayout.OnePerLine));
        var formatter = new ScriptDomSqlFormatter();

        var first = formatter.Format(source, options, new FormatRequest());
        var second = formatter.Format(first.Text, options, new FormatRequest());

        Assert.Equal(expected, first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }
}
