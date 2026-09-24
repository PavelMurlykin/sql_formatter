using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class OutputFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("insert into T (Id) output inserted.Id values (1);",
        "INSERT INTO T (Id)\nOUTPUT inserted.Id\nVALUES\n    (1);")]
    [InlineData("insert into T (Id) output inserted.Id into dbo.Audit (Id) values (1);",
        "INSERT INTO T (Id)\nOUTPUT inserted.Id INTO dbo.Audit (Id)\nVALUES\n    (1);")]
    [InlineData("update T set Id=2 output inserted.Id,deleted.Id where Id=1;",
        "UPDATE T\nSET\n    Id = 2\nOUTPUT inserted.Id, deleted.Id\nWHERE\n    Id = 1;")]
    [InlineData("delete from T output deleted.Id into dbo.Log (Id) where Id=1;",
        "DELETE FROM T\nOUTPUT deleted.Id INTO dbo.Log (Id)\nWHERE\n    Id = 1;")]
    public void Formats_output_across_dml(string source, string expected)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Preserves_output_keyword_spelling()
    {
        var result = _formatter.Format("delete from T output deleted.Id where Id=1",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("delete from T\noutput deleted.Id\nwhere\n    Id = 1", result.Text);
    }

    [Fact]
    public void Leaves_comment_between_output_items_in_original_layout()
    {
        const string source = "update T set Id=2 output inserted.Id, /* keep */ deleted.Id";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal(source, result.Text);
    }
}
