using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class SelectFromParityTests
{
    private static string Format(string sql, params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        var options = FormattingOptions.Default.With(rules: values);
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        return first.Text;
    }

    private static (string, RuleValue) Choice(string key, string value) => (key, RuleValue.FromChoice(value));
    private static (string, RuleValue) Indent(string key, int offset, bool onNewLineOnly = true,
        string style = "relative", bool transparent = false) => (key,
        RuleValue.FromIndent(new IndentRule(true, offset, onNewLineOnly, style, transparent)));

    [Fact]
    public void Into_keyword_and_table_boundaries_are_independent()
    {
        const string sql = "SELECT a INTO #tmp FROM dbo.T";
        var before = Format(sql, Choice("select.into.breakBefore", "always"));
        var after = Format(sql, Choice("select.into.breakAfter", "always"));
        Assert.Contains("a\nINTO", before);
        Assert.Contains("INTO\n", after);
        Assert.Contains("#tmp", after);
    }

    [Fact]
    public void From_keyword_and_list_boundaries_are_independent()
    {
        const string sql = "SELECT a FROM dbo.T";
        var before = Format(sql, Choice("select.from.breakBefore", "never"));
        var after = Format(sql, Choice("select.from.breakAfter", "always"));
        Assert.Contains("a FROM", before);
        Assert.Contains("FROM\n", after);
    }

    [Fact]
    public void From_list_can_be_stacked_or_compact()
    {
        const string sql = "SELECT a.Id FROM dbo.A a, dbo.B b";
        var stacked = Format(sql, Choice("select.from.stackList", "on"),
            Choice("select.from.stackMode", "onePerLine"));
        var compact = Format(sql, Choice("select.from.stackList", "off"));
        Assert.Contains(",\n", stacked);
        Assert.Contains(", ", compact);
    }

    [Fact]
    public void From_stack_mode_auto_keeps_a_short_list_inline()
    {
        const string sql = "SELECT a.Id FROM dbo.A a, dbo.B b";
        var automatic = Format(sql, Choice("select.from.stackList", "on"),
            Choice("select.from.stackMode", "auto"));
        var vertical = Format(sql, Choice("select.from.stackList", "on"),
            Choice("select.from.stackMode", "onePerLine"));
        Assert.Contains(", ", automatic);
        Assert.Contains(",\n", vertical);
    }

    [Fact]
    public void Join_and_on_boundaries_are_independent()
    {
        const string sql = "SELECT a.Id FROM dbo.A a INNER JOIN dbo.B b ON a.Id = b.Id";
        var beforeJoin = Format(sql, Choice("select.join.breakBefore", "never"));
        var afterJoin = Format(sql, Choice("select.join.breakAfter", "always"));
        var beforeOn = Format(sql, Choice("select.join.onBreakBefore", "never"));
        var afterOn = Format(sql, Choice("select.join.onBreakAfter", "always"));
        Assert.Contains("a INNER JOIN", beforeJoin);
        Assert.Contains("JOIN\n", afterJoin);
        Assert.Contains("b ON", beforeOn);
        Assert.Contains("ON\n", afterOn);
    }

    [Fact]
    public void Apply_uses_join_keyword_and_table_rules_without_on()
    {
        var text = Format("SELECT a.Id FROM dbo.A a CROSS APPLY dbo.fn(a.Id) f",
            Choice("select.join.breakAfter", "always"));
        Assert.Contains("APPLY\n", text);
    }

    [Fact]
    public void Join_boolean_wrap_modes_and_operator_sides_are_distinct()
    {
        const string sql = "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id AND a.K=b.K";
        var leading = Format(sql, Choice("select.join.wrapCondition", "and"));
        var trailing = Format(sql, Choice("select.join.wrapBeforeOperator", "never"),
            Choice("select.join.wrapAfterOperator", "always"));
        Assert.Matches("\\n[ \\t]*AND ", leading);
        Assert.Contains("AND\n", trailing);
    }

    [Fact]
    public void Join_wrap_condition_can_target_or_without_changing_and()
    {
        const string sql = "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id AND a.K=b.K OR a.X=b.X";
        var text = Format(sql, Choice("select.join.wrapCondition", "or"));
        Assert.Matches("\\n[ \\t]*OR ", text);
        Assert.Contains("AND", text);
    }

    [Theory]
    [InlineData("select.into.keywordIndent", "SELECT a INTO #tmp FROM dbo.T", "select.into.breakBefore")]
    [InlineData("select.into.tableIndent", "SELECT a INTO #tmp FROM dbo.T", "select.into.breakAfter")]
    [InlineData("select.from.keywordIndent", "SELECT a FROM dbo.T", "select.from.breakBefore")]
    [InlineData("select.from.listIndent", "SELECT a FROM dbo.T", "select.from.breakAfter")]
    [InlineData("select.join.keywordIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id", "select.join.breakBefore")]
    [InlineData("select.join.tableIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id", "select.join.breakAfter")]
    [InlineData("select.join.onKeywordIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id", "select.join.onBreakBefore")]
    [InlineData("select.join.onConditionIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id", "select.join.onBreakAfter")]
    public void Local_indent_changes_its_own_boundary(string indentKey, string sql, string breakKey)
    {
        var baseline = Format(sql, Choice(breakKey, "always"));
        var changed = Format(sql, Choice(breakKey, "always"), Indent(indentKey, 2));
        Assert.NotEqual(baseline, changed);
    }

    [Theory]
    [InlineData("select.into.keywordIndent", "SELECT a INTO #tmp FROM dbo.T", "select.into.breakBefore")]
    [InlineData("select.into.tableIndent", "SELECT a INTO #tmp FROM dbo.T", "select.into.breakAfter")]
    [InlineData("select.from.keywordIndent", "SELECT a FROM dbo.T", "select.from.breakBefore")]
    [InlineData("select.from.listIndent", "SELECT a FROM dbo.T", "select.from.breakAfter")]
    [InlineData("select.join.keywordIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id", "select.join.breakBefore")]
    [InlineData("select.join.tableIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id", "select.join.breakAfter")]
    [InlineData("select.join.onKeywordIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id", "select.join.onBreakBefore")]
    [InlineData("select.join.onConditionIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id", "select.join.onBreakAfter")]
    [InlineData("select.join.nestedConditionIndent", "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id AND a.K=b.K", "select.join.wrapAfterOperator")]
    public void On_new_line_only_controls_inline_padding(string indentKey, string sql, string breakKey)
    {
        var lineOnly = Format(sql, Choice(breakKey, "never"), Indent(indentKey, 1));
        var alsoInline = Format(sql, Choice(breakKey, "never"),
            Indent(indentKey, 1, onNewLineOnly: false));
        Assert.NotEqual(lineOnly, alsoInline);
    }

    [Fact]
    public void Join_keyword_indent_supports_style_transparency_and_inline_mode()
    {
        const string sql = "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id";
        var absolute = Format(sql, Choice("select.join.breakBefore", "always"),
            Indent("select.join.keywordIndent", 2, style: "absolute"));
        var transparent = Format(sql, Choice("select.join.breakBefore", "always"),
            Indent("select.join.keywordIndent", 2, style: "absolute", transparent: true));
        var inline = Format(sql, Choice("select.join.breakBefore", "never"),
            Indent("select.join.keywordIndent", 1, onNewLineOnly: false));
        Assert.Contains("\n        JOIN", absolute);
        Assert.Contains("\nJOIN", transparent);
        Assert.Contains("a     JOIN", inline);
    }

    [Fact]
    public void Nested_condition_indent_changes_wrapped_operator_operand()
    {
        const string sql = "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id AND a.K=b.K";
        var baseline = Format(sql, Choice("select.join.wrapAfterOperator", "always"));
        var indented = Format(sql, Choice("select.join.wrapAfterOperator", "always"),
            Indent("select.join.nestedConditionIndent", 2));
        Assert.NotEqual(baseline, indented);
    }

    [Fact]
    public void Json_version_two_accepts_scoped_join_rules()
    {
        var options = new SqlFormatterConfigurationSerializer().Deserialize(
            """{"version":2,"rules":{"select.join.breakBefore":"never"}}""");
        Assert.Equal("never", options.Rules.Get("select.join.breakBefore").Choice);
    }

    [Fact]
    public void Literal_and_comment_text_are_preserved_while_from_rule_runs()
    {
        var text = Format("SELECT 'FROM JOIN' AS x -- FROM stays\nFROM dbo.T",
            Choice("select.from.breakAfter", "always"));
        Assert.Contains("'FROM JOIN'", text);
        Assert.Contains("-- FROM stays", text);
    }
}
