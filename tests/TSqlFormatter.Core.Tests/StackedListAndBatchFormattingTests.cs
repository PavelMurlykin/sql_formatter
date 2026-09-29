using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class StackedListAndBatchFormattingTests
{
    private static string Format(string source, string rules, string extra = "")
    {
        var json = "{\"version\":2," + extra + "\"rules\":{" + rules + "}}";
        var options = new SqlFormatterConfigurationSerializer().Deserialize(json);
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(source, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        return first.Text;
    }

    [Fact]
    public void Vertical_select_list_can_use_leading_comma_with_or_without_space()
    {
        const string sql = "SELECT a, b, c FROM dbo.T";
        const string list = "\"select\":{\"columns\":\"onePerLine\"},";
        var spaced = Format(sql, "\"stackedList.commaPlacement\":\"leading\"", list);
        var compact = Format(sql,
            "\"stackedList.commaPlacement\":\"leading\","
            + "\"stackedList.spaceAfterLeadingComma\":\"remove\"", list);

        Assert.Contains("a\n    , b\n    , c", spaced);
        Assert.Contains("a\n    ,b\n    ,c", compact);
        Assert.Contains("FROM dbo.T", compact);
    }

    [Fact]
    public void Explicit_trailing_policy_converts_an_existing_leading_list()
    {
        var text = Format("SELECT a\n    , b\n    , c FROM dbo.T",
            "\"stackedList.commaPlacement\":\"trailing\"",
            "\"select\":{\"columns\":\"onePerLine\"},");
        Assert.Contains("a,\n", text);
        Assert.DoesNotContain("\n    ,", text);
    }

    [Fact]
    public void Leading_comma_spacing_can_be_removed()
    {
        var text = Format("SELECT a\n    , b FROM dbo.T",
            "\"stackedList.commaPlacement\":\"leading\","
            + "\"stackedList.spaceAfterLeadingComma\":\"remove\"",
            "\"select\":{\"columns\":\"onePerLine\"},");
        Assert.Contains(",b", text);
    }

    [Theory]
    [InlineData("after", "SELECT 1;\nGO\n\nSELECT 2;")]
    [InlineData("before", "SELECT 1;\n\nGO\nSELECT 2;")]
    [InlineData("both", "SELECT 1;\n\nGO\n\nSELECT 2;")]
    public void Go_blank_line_mode_is_configurable(string mode, string expected)
    {
        var text = Format("SELECT 1;\nGO\nSELECT 2;",
            "\"misc.packageDelimiterBlankLine\":true,"
            + "\"misc.packageDelimiterBlankLineMode\":\"" + mode + "\"");
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Disabled_go_rule_preserves_original_batch_spacing()
    {
        Assert.Equal("SELECT 1;\nGO\nSELECT 2;", Format("SELECT 1;\nGO\nSELECT 2;",
            "\"misc.packageDelimiterBlankLineMode\":\"both\""));
    }

    [Fact]
    public void Go_rule_respects_existing_blank_lines_crlf_and_comments()
    {
        var text = Format("SELECT 1;\r\n\r\nGO -- batch\r\n\r\nSELECT 2;",
            "\"misc.packageDelimiterBlankLine\":true,"
            + "\"misc.packageDelimiterBlankLineMode\":\"both\"");
        Assert.Contains("\r\n\r\nGO -- batch\r\n\r\n", text);
        Assert.DoesNotContain("\r\n\r\n\r\n", text);
    }

    [Fact]
    public void Go_in_a_string_or_comment_is_not_a_batch_delimiter()
    {
        var text = Format("SELECT 'GO' AS x; -- GO\nSELECT 2;",
            "\"misc.packageDelimiterBlankLine\":true");
        Assert.DoesNotContain("\n\n", text);
        Assert.Contains("'GO'", text);
        Assert.Contains("-- GO", text);
    }

    [Fact]
    public void Comments_and_commas_inside_literals_are_untouched()
    {
        var text = Format("SELECT 'a,b' AS x, -- comment,\n y FROM dbo.T",
            "\"stackedList.commaPlacement\":\"leading\","
            + "\"stackedList.spaceAfterLeadingComma\":\"remove\"");
        Assert.Contains("'a,b'", text);
        Assert.Contains("-- comment,", text);
    }

    [Fact]
    public void Four_stage_rules_are_registered()
    {
        Assert.Equal(2, RuleCatalog.Default.Definitions.Keys.Count(key => key.StartsWith("stackedList.", StringComparison.Ordinal)));
        Assert.Equal(2, RuleCatalog.Default.Definitions.Keys.Count(key => key.StartsWith("misc.", StringComparison.Ordinal)));
    }
}
