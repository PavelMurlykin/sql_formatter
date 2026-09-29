using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class SelectListParityTests
{
    private static string Format(string sql, string rules, string other = "")
    {
        var options = new SqlFormatterConfigurationSerializer().Deserialize(
            "{\"version\":2," + other + "\"rules\":{" + rules + "}}");
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        return first.Text;
    }

    [Fact]
    public void Character_threshold_compacts_only_short_queries()
    {
        const string rule = "\"select.singleLine.maxCharacters\":{\"enabled\":true,\"value\":35}";
        Assert.Equal("SELECT a, b FROM dbo.T", Format("select a,b from dbo.T", rule));
        Assert.Contains("\nFROM", Format("select longerColumn, anotherColumn from dbo.LongTable", rule));
    }

    [Fact]
    public void Word_threshold_and_right_margin_have_independent_effect()
    {
        var shortWords = Format("SELECT a, b FROM dbo.T",
            "\"select.singleLine.maxWords\":{\"enabled\":true,\"value\":2}");
        var enoughWords = Format("SELECT a, b FROM dbo.T",
            "\"select.singleLine.maxWords\":{\"enabled\":true,\"value\":20}");
        var fits = Format("SELECT a, b FROM dbo.T",
            "\"select.singleLine.whenFitsMargin\":true");
        Assert.Contains("\nFROM", shortWords);
        Assert.Equal("SELECT a, b FROM dbo.T", enoughWords);
        Assert.Equal(enoughWords, fits);
    }

    [Fact]
    public void Compactness_never_exceeds_configured_margin()
    {
        var text = Format("SELECT a, b FROM dbo.T",
            "\"select.singleLine.whenFitsMargin\":true",
            "\"general\":{\"maxLineLength\":16},");
        Assert.Contains("\n", text);
    }

    [Fact]
    public void First_column_break_can_be_forced_or_suppressed()
    {
        var newline = Format("SELECT a, b FROM dbo.T",
            "\"select.list.breakBeforeFirstColumn\":\"always\"");
        var inline = Format("SELECT a, b FROM dbo.T",
            "\"select.list.breakBeforeFirstColumn\":\"never\","
            + "\"select.list.stackColumns\":\"on\","
            + "\"select.list.stackMode\":\"onePerLine\"");
        Assert.StartsWith("SELECT\n", newline);
        Assert.StartsWith("SELECT a,\n", inline);
    }

    [Fact]
    public void List_indent_offset_and_inline_indent_are_observable()
    {
        var indented = Format("SELECT a, b FROM dbo.T",
            "\"select.list.breakBeforeFirstColumn\":\"always\","
            + "\"select.list.indent\":{\"enabled\":true,\"offset\":2,"
            + "\"onNewLineOnly\":true,\"style\":\"absolute\",\"transparent\":false}");
        var inline = Format("SELECT a, b FROM dbo.T",
            "\"select.list.breakBeforeFirstColumn\":\"never\","
            + "\"select.list.indent\":{\"enabled\":true,\"offset\":1,"
            + "\"onNewLineOnly\":false,\"style\":\"relative\",\"transparent\":false}");
        Assert.StartsWith("SELECT\n        a", indented);
        Assert.StartsWith("SELECT         a", inline);
    }

    [Fact]
    public void Stack_switch_and_mode_change_column_layout()
    {
        var stacked = Format("SELECT a, b FROM dbo.T",
            "\"select.list.stackColumns\":\"on\","
            + "\"select.list.stackMode\":\"onePerLine\"");
        var automatic = Format("SELECT a, b FROM dbo.T",
            "\"select.list.stackColumns\":\"on\","
            + "\"select.list.stackMode\":\"auto\"");
        var off = Format("SELECT a, b FROM dbo.T",
            "\"select.list.stackColumns\":\"off\"");
        Assert.Contains("a,\n", stacked);
        Assert.Contains("a, b", automatic);
        Assert.Contains("a, b", off);
    }

    [Fact]
    public void Comment_prevents_whole_statement_compaction()
    {
        var text = Format("SELECT a, -- keep\n b FROM dbo.T",
            "\"select.singleLine.whenFitsMargin\":true");
        Assert.Contains("-- keep\n", text);
    }

    [Fact]
    public void Seven_native_rules_cover_select_list_and_single_line_settings()
    {
        Assert.Equal(7, RuleCatalog.Default.Definitions.Keys.Count(key =>
            key.StartsWith("select.list.", StringComparison.Ordinal)
            || key.StartsWith("select.singleLine.", StringComparison.Ordinal)));
    }
}
