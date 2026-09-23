using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class ParenthesesFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Fact]
    public void Formats_nested_boolean_parentheses()
    {
        const string source = "select Id from T where (A=1 or (B=2 and C=3)) and D=4";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM T\nWHERE\n    (\n        A = 1\n        OR (\n            B = 2\n            AND C = 3\n        )\n    )\n    AND D = 4", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Formats_parenthesized_query_expression_in_derived_table()
    {
        const string source = "select d.Id from ((select Id from T)) as d";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT d.Id\nFROM (\n    (\n        SELECT Id\n        FROM T\n    )\n) AS d", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Parenthesized_where_is_idempotent()
    {
        const string source = "select Id from T where (A=1 or B=2) and C=3";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }
}
