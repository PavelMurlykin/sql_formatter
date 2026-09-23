using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class InsertFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("insert into dbo.T (Id, Name) values (1, 'a'), (2, 'b');",
        "INSERT INTO dbo.T (Id, Name)\nVALUES\n    (1, 'a'),\n    (2, 'b');")]
    [InlineData("insert T values(1)", "INSERT T\nVALUES\n    (1)")]
    [InlineData("insert into T (A,B) values (@a,@b)",
        "INSERT INTO T (A, B)\nVALUES\n    (@a, @b)")]
    public void Formats_insert_values_and_column_lists(string source, string expected)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Formats_insert_select()
    {
        const string source = "insert into dbo.T (Id, Name) select Id, Name from U;";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("INSERT INTO dbo.T (Id, Name)\nSELECT Id, Name\nFROM U;", result.Text);
    }

    [Fact]
    public void Preserves_insert_keyword_spelling_when_requested()
    {
        var result = _formatter.Format("insert into T (Id) values (1)",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("insert into T (Id)\nvalues\n    (1)", result.Text);
    }

    [Fact]
    public void Leaves_unsupported_insert_source_in_original_layout()
    {
        const string source = "insert into T exec dbo.GetRows";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal(source, result.Text);
    }

    [Fact]
    public void Leaves_comments_between_values_in_original_layout()
    {
        const string source = "insert into T values (1), /* keep */ (2)";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal(source, result.Text);
    }
}
