using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class CaseFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Theory]
    [InlineData("select case Status when 1 then 'New' when 2 then 'Done' else 'Other' end as Label from T",
        "SELECT\n    CASE Status\n        WHEN 1\n            THEN 'New'\n        WHEN 2\n            THEN 'Done'\n        ELSE 'Other'\n    END AS Label\nFROM T")]
    [InlineData("select case when Score>=90 then 'A' when Score>=70 then 'B' else 'C' end as Grade from T",
        "SELECT\n    CASE\n        WHEN Score>=90\n            THEN 'A'\n        WHEN Score>=70\n            THEN 'B'\n        ELSE 'C'\n    END AS Grade\nFROM T")]
    [InlineData("select case when Flag=1 then 'Y' end from T",
        "SELECT\n    CASE\n        WHEN Flag=1\n            THEN 'Y'\n    END\nFROM T")]
    public void Formats_simple_and_searched_case(string source, string expected)
    {
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal(expected, first.Text);
        Assert.Equal(first.Text, second.Text);
        Assert.True(first.ParseSucceeded);
    }

    [Fact]
    public void Preserves_case_keyword_spelling_when_requested()
    {
        var result = _formatter.Format("select case when Flag=1 then 'Y' end from T",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("select\n    case\n        when Flag=1\n            then 'Y'\n    end\nfrom T", result.Text);
    }

    [Fact]
    public void Leaves_case_with_inline_comment_in_original_layout()
    {
        const string source = "select case when X=1 /* note */ then 2 else 3 end from T";
        var result = _formatter.Format(source, new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT CASE WHEN X=1 /* note */ THEN 2 ELSE 3 END FROM T", result.Text);
    }

    [Fact]
    public void Formats_case_as_comparison_operand()
    {
        var result = _formatter.Format("select Id from T where case when X=1 then 2 else 3 end=2",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT Id\nFROM T\nWHERE\n    CASE\n        WHEN X=1\n            THEN 2\n        ELSE 3\n    END = 2", result.Text);
    }
}
