using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class SelectClauseParityTests
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
    private static (string, RuleValue) Indent(string key, int offset, bool onlyNewline = true) =>
        (key, RuleValue.FromIndent(new IndentRule(true, offset, onlyNewline)));

    [Theory]
    [InlineData("select.where", "SELECT a FROM dbo.T WHERE a=1", "WHERE")]
    [InlineData("select.having", "SELECT a FROM dbo.T GROUP BY a HAVING COUNT(*)>1", "HAVING")]
    public void Condition_clause_has_independent_before_and_after_breaks(string prefix, string sql, string keyword)
    {
        var before = Format(sql, Choice(prefix + ".breakBefore", "never"));
        var after = Format(sql, Choice(prefix + ".breakAfter", "always"));
        Assert.DoesNotContain("\n" + keyword, before);
        Assert.Contains(keyword + "\n", after);
    }

    [Theory]
    [InlineData("select.groupBy", "SELECT a, b FROM dbo.T GROUP BY a, b", "GROUP BY")]
    [InlineData("select.orderBy", "SELECT a, b FROM dbo.T ORDER BY a, b", "ORDER BY")]
    public void List_clause_has_independent_before_after_and_stack_rules(string prefix, string sql, string keyword)
    {
        var before = Format(sql, Choice(prefix + ".breakBefore", "never"));
        var after = Format(sql, Choice(prefix + ".breakAfter", "always"));
        var stacked = Format(sql, Choice(prefix + ".stackList", "on"),
            Choice(prefix + ".stackMode", "onePerLine"));
        var compact = Format(sql, Choice(prefix + ".stackList", "off"));
        Assert.DoesNotContain("\n" + keyword, before);
        Assert.Contains(keyword + "\n", after);
        Assert.Contains(",\n", stacked);
        Assert.Contains(", ", compact);
    }

    [Theory]
    [InlineData("select.groupBy", "SELECT a, b FROM dbo.T GROUP BY a, b")]
    [InlineData("select.orderBy", "SELECT a, b FROM dbo.T ORDER BY a, b")]
    public void Auto_stack_mode_keeps_a_short_clause_inline(string prefix, string sql)
    {
        var auto = Format(sql, Choice(prefix + ".stackList", "on"),
            Choice(prefix + ".stackMode", "auto"));
        var onePerLine = Format(sql, Choice(prefix + ".stackList", "on"),
            Choice(prefix + ".stackMode", "onePerLine"));
        Assert.Contains(", ", auto);
        Assert.Contains(",\n", onePerLine);
    }

    [Fact]
    public void Where_and_having_wrapping_do_not_leak_between_clauses()
    {
        const string sql = "SELECT a FROM dbo.T WHERE a=1 AND b=2 GROUP BY a HAVING COUNT(*)>1 AND MAX(b)>2";
        var whereOnly = Format(sql, Choice("select.where.wrapCondition", "none"));
        var havingOnly = Format(sql, Choice("select.having.wrapCondition", "none"));
        Assert.Contains("a = 1 AND", whereOnly);
        Assert.Contains("COUNT(*) > 1 AND", havingOnly);
        Assert.NotEqual(whereOnly, havingOnly);
    }

    [Theory]
    [InlineData("select.where.keywordIndent", "SELECT a FROM dbo.T WHERE a=1", "select.where.breakBefore")]
    [InlineData("select.where.conditionIndent", "SELECT a FROM dbo.T WHERE a=1", "select.where.breakAfter")]
    [InlineData("select.having.keywordIndent", "SELECT a FROM dbo.T GROUP BY a HAVING COUNT(*)>1", "select.having.breakBefore")]
    [InlineData("select.having.conditionIndent", "SELECT a FROM dbo.T GROUP BY a HAVING COUNT(*)>1", "select.having.breakAfter")]
    [InlineData("select.groupBy.keywordIndent", "SELECT a FROM dbo.T GROUP BY a", "select.groupBy.breakBefore")]
    [InlineData("select.groupBy.listIndent", "SELECT a FROM dbo.T GROUP BY a", "select.groupBy.breakAfter")]
    [InlineData("select.orderBy.keywordIndent", "SELECT a FROM dbo.T ORDER BY a", "select.orderBy.breakBefore")]
    [InlineData("select.orderBy.listIndent", "SELECT a FROM dbo.T ORDER BY a", "select.orderBy.breakAfter")]
    public void Local_indent_and_inline_policy_are_observable(string indentKey, string sql, string breakKey)
    {
        var baseline = Format(sql, Choice(breakKey, "always"));
        var indented = Format(sql, Choice(breakKey, "always"), Indent(indentKey, 2));
        var lineOnly = Format(sql, Choice(breakKey, "never"), Indent(indentKey, 1));
        var alsoInline = Format(sql, Choice(breakKey, "never"), Indent(indentKey, 1, false));
        Assert.NotEqual(baseline, indented);
        Assert.NotEqual(lineOnly, alsoInline);
    }

    [Theory]
    [InlineData("select.where", "SELECT a FROM dbo.T WHERE a=1 AND b=2")]
    [InlineData("select.having", "SELECT a FROM dbo.T GROUP BY a HAVING COUNT(*)>1 AND MAX(b)>2")]
    public void Nested_condition_indent_and_operator_side_rules_are_observable(string prefix, string sql)
    {
        var before = Format(sql, Choice(prefix + ".wrapBeforeOperator", "always"));
        var after = Format(sql, Choice(prefix + ".wrapAfterOperator", "always"));
        var indented = Format(sql, Choice(prefix + ".wrapAfterOperator", "always"),
            Indent(prefix + ".nestedConditionIndent", 2));
        Assert.NotEqual(before, after);
        Assert.NotEqual(after, indented);
    }

    [Theory]
    [InlineData("select.where", "SELECT a FROM dbo.T WHERE a=1 AND b=2")]
    [InlineData("select.having", "SELECT a FROM dbo.T GROUP BY a HAVING COUNT(*)>1 AND MAX(b)>2")]
    public void Nested_condition_on_new_line_only_changes_inline_operand(string prefix, string sql)
    {
        var onlyNewline = Format(sql, Choice(prefix + ".wrapAfterOperator", "never"),
            Indent(prefix + ".nestedConditionIndent", 1));
        var alsoInline = Format(sql, Choice(prefix + ".wrapAfterOperator", "never"),
            Indent(prefix + ".nestedConditionIndent", 1, false));
        Assert.NotEqual(onlyNewline, alsoInline);
    }

    [Fact]
    public void Clause_rules_do_not_change_literals_or_comments()
    {
        var text = Format("SELECT 'WHERE, HAVING' AS x FROM dbo.T -- WHERE comment\nWHERE x=1",
            Choice("select.where.breakAfter", "always"));
        Assert.Contains("'WHERE, HAVING'", text);
        Assert.Contains("-- WHERE comment", text);
    }
}
