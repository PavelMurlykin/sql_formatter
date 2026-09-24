using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class UpdateFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("update T set Name='x',Count=2 where Id=1;",
        "UPDATE T\nSET\n    Name = 'x',\n    Count = 2\nWHERE\n    Id = 1;")]
    [InlineData("update t set Name='x',Count=2 from dbo.T t join dbo.U u on t.Id=u.Id where t.Id=1;",
        "UPDATE t\nSET\n    Name = 'x',\n    Count = 2\nFROM dbo.T t\nJOIN dbo.U u\n    ON t.Id = u.Id\nWHERE\n    t.Id = 1;")]
    [InlineData("update T set A=1", "UPDATE T\nSET\n    A = 1")]
    public void Formats_update_set_from_where(string source, string expected)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Preserves_keyword_spelling()
    {
        var result = _formatter.Format("update T set A=1 where Id=2",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("update T\nset\n    A = 1\nwhere\n    Id = 2", result.Text);
    }

    [Fact]
    public void Leaves_unsupported_assignment_in_original_layout()
    {
        const string source = "update T set A += 1";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal(source, result.Text);
    }
}
