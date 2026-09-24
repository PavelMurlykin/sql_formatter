using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class DeleteFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("delete from T where Id=1;", "DELETE FROM T\nWHERE\n    Id = 1;")]
    [InlineData("delete t from dbo.T t join dbo.U u on t.Id=u.Id where u.Flag=1;",
        "DELETE t\nFROM dbo.T t\nJOIN dbo.U u\n    ON t.Id = u.Id\nWHERE\n    u.Flag = 1;")]
    [InlineData("delete from T", "DELETE FROM T")]
    public void Formats_delete_from_join_where(string source, string expected)
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
        var result = _formatter.Format("delete from T where Id=1",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("delete from T\nwhere\n    Id = 1", result.Text);
    }

    [Fact]
    public void Leaves_top_delete_in_original_layout()
    {
        const string source = "delete top (2) from T where Id=1";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal(source, result.Text);
    }
}
