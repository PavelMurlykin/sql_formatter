using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SelectTailParityTests
{
    private static string Format(string sql, params (string Key, RuleValue Value)[] rules) =>
        FormatWithDialect(sql, SqlDialectVersion.Auto, rules);

    private static string FormatWithDialect(string sql, SqlDialectVersion dialect,
        params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        var options = FormattingOptions.Default.With(rules: values);
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest(dialect: dialect));
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options,
            new FormatRequest(dialect: dialect)).Text);
        return first.Text;
    }

    private static (string, RuleValue) Choice(string key, string value) => (key, RuleValue.FromChoice(value));
    private static (string, RuleValue) Indent(string key, int offset, bool onlyNewline = true) =>
        (key, RuleValue.FromIndent(new IndentRule(true, offset, onlyNewline)));

    [Fact]
    public void Cte_column_braces_list_and_as_have_independent_breaks()
    {
        const string sql = "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte";
        var beforeOpen = Format(sql, Choice("select.cte.breakBeforeColumnOpen", "always"));
        var afterOpen = Format(sql, Choice("select.cte.breakAfterColumnOpen", "always"));
        var beforeClose = Format(sql, Choice("select.cte.breakBeforeColumnClose", "always"));
        var beforeAs = Format(sql, Choice("select.cte.breakBeforeAs", "never"));
        var afterAs = Format(sql, Choice("select.cte.breakAfterAs", "never"));
        Assert.Contains("cte\n(", beforeOpen);
        Assert.Contains("(\n", afterOpen);
        Assert.Contains("b\n)", beforeClose);
        Assert.Contains(") AS", beforeAs);
        Assert.Contains("AS (", afterAs);
    }

    [Fact]
    public void Cte_with_and_column_stack_modes_are_configurable()
    {
        const string sql = "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte";
        var afterWith = Format(sql, Choice("select.cte.breakAfterWith", "always"));
        var stacked = Format(sql, Choice("select.cte.stackColumns", "on"),
            Choice("select.cte.stackMode", "onePerLine"));
        var automatic = Format(sql, Choice("select.cte.stackColumns", "on"),
            Choice("select.cte.stackMode", "auto"));
        Assert.Contains("WITH\n", afterWith);
        Assert.Contains(",\n", stacked);
        Assert.Contains(", ", automatic);
    }

    [Fact]
    public void For_xml_and_option_boundaries_are_configurable()
    {
        var xml = Format("SELECT a FROM dbo.T FOR XML PATH('row')",
            Choice("select.for.breakBefore", "never"),
            Choice("select.for.breakAfterXml", "always"));
        var option = Format("SELECT a FROM dbo.T OPTION (RECOMPILE)",
            Choice("select.option.breakBefore", "never"),
            Choice("select.option.breakAfter", "always"));
        Assert.Contains("T FOR XML\n", xml);
        Assert.Contains("T OPTION\n", option);
    }

    [Fact]
    public void Compute_uses_sql2008_parser_in_auto_and_rejects_modern_dialect_explicitly()
    {
        const string sql = "SELECT a FROM dbo.T COMPUTE SUM(a)";
        var parsed = new ScriptDomSqlParser().Parse(sql, SqlDialectVersion.Auto);
        Assert.True(parsed.ParseSucceeded);
        Assert.Equal(SqlVersion.Sql100, parsed.ParserVersion);
        var formatted = Format(sql, Choice("select.compute.breakBefore", "always"),
            Choice("select.compute.breakAfter", "always"));
        Assert.Contains("\nCOMPUTE\n", formatted);
        Assert.Equal(formatted, FormatWithDialect(sql, SqlDialectVersion.Sql2008,
            Choice("select.compute.breakBefore", "always"),
            Choice("select.compute.breakAfter", "always")));

        var modern = new ScriptDomSqlFormatter().Format(sql, FormattingOptions.Default,
            new FormatRequest(dialect: SqlDialectVersion.Sql2019));
        Assert.False(modern.ParseSucceeded);
        Assert.Equal(sql, modern.Text);
        Assert.Contains(modern.Diagnostics, diagnostic => diagnostic.Code == "TSF3005");
    }

    [Theory]
    [InlineData("select.cte.columnListIndent", "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte", "select.cte.breakAfterColumnOpen")]
    [InlineData("select.cte.columnBraceIndent", "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte", "select.cte.breakBeforeColumnOpen")]
    [InlineData("select.cte.expressionIndent", "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte", "select.cte.breakAfterWith")]
    [InlineData("select.cte.subqueryBraceIndent", "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte", "select.cte.breakAfterAs")]
    [InlineData("select.for.keywordIndent", "SELECT a FROM dbo.T FOR XML PATH('row')", "select.for.breakBefore")]
    [InlineData("select.for.specIndent", "SELECT a FROM dbo.T FOR XML PATH('row')", "select.for.breakAfterXml")]
    [InlineData("select.option.keywordIndent", "SELECT a FROM dbo.T OPTION (RECOMPILE)", "select.option.breakBefore")]
    [InlineData("select.compute.keywordIndent", "SELECT a FROM dbo.T COMPUTE SUM(a)", "select.compute.breakBefore")]
    [InlineData("select.compute.expressionIndent", "SELECT a FROM dbo.T COMPUTE SUM(a)", "select.compute.breakAfter")]
    public void Scoped_indent_changes_its_boundary(string indentKey, string sql, string breakKey)
    {
        var baseline = Format(sql, Choice(breakKey, "always"));
        var indented = Format(sql, Choice(breakKey, "always"), Indent(indentKey, 2));
        Assert.NotEqual(baseline, indented);
    }

    [Fact]
    public void Option_hint_indent_changes_a_multiline_hint()
    {
        const string sql = "SELECT a FROM dbo.T OPTION (\nRECOMPILE)";
        var baseline = Format(sql, Choice("select.option.breakAfter", "never"));
        var indented = Format(sql, Choice("select.option.breakAfter", "never"),
            Indent("select.option.hintsIndent", 2));
        Assert.NotEqual(baseline, indented);
    }

    [Theory]
    [InlineData("select.cte.columnListIndent", "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte", "select.cte.breakAfterColumnOpen")]
    [InlineData("select.cte.columnBraceIndent", "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte", "select.cte.breakBeforeColumnOpen")]
    [InlineData("select.cte.expressionIndent", "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte", "select.cte.breakAfterWith")]
    [InlineData("select.cte.subqueryBraceIndent", "WITH cte(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM cte", "select.cte.breakAfterAs")]
    [InlineData("select.for.keywordIndent", "SELECT a FROM dbo.T FOR XML PATH('row')", "select.for.breakBefore")]
    [InlineData("select.for.specIndent", "SELECT a FROM dbo.T FOR XML PATH('row')", "select.for.breakAfterXml")]
    [InlineData("select.option.keywordIndent", "SELECT a FROM dbo.T OPTION (RECOMPILE)", "select.option.breakBefore")]
    [InlineData("select.compute.keywordIndent", "SELECT a FROM dbo.T COMPUTE SUM(a)", "select.compute.breakBefore")]
    [InlineData("select.compute.expressionIndent", "SELECT a FROM dbo.T COMPUTE SUM(a)", "select.compute.breakAfter")]
    public void Scoped_indent_on_new_line_only_controls_inline_padding(string indentKey, string sql,
        string breakKey)
    {
        var lineOnly = Format(sql, Choice(breakKey, "never"), Indent(indentKey, 1));
        var inline = Format(sql, Choice(breakKey, "never"),
            (indentKey, RuleValue.FromIndent(new IndentRule(true, 1, false))));
        Assert.NotEqual(lineOnly, inline);
    }

    [Fact]
    public void Option_hint_inline_padding_is_opt_in()
    {
        const string sql = "SELECT a FROM dbo.T OPTION (RECOMPILE)";
        var lineOnly = Format(sql, Indent("select.option.hintsIndent", 1));
        var inline = Format(sql, ("select.option.hintsIndent",
            RuleValue.FromIndent(new IndentRule(true, 1, false))));
        Assert.NotEqual(lineOnly, inline);
    }

    [Fact]
    public void Cte_literals_and_comments_are_preserved()
    {
        var text = Format("WITH cte(a,b) AS (SELECT 'AS,WITH',b FROM dbo.T) SELECT a FROM cte -- AS",
            Choice("select.cte.breakBeforeAs", "always"));
        Assert.Contains("'AS,WITH'", text);
        Assert.Contains("-- AS", text);
    }
}
