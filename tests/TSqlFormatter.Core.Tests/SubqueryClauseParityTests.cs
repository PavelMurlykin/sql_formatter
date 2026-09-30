using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SubqueryClauseParityTests
{
    [Fact]
    public void Ledger_maps_all_supported_nested_clause_paths_to_native_rules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "coverage.tsv");
        var rows = File.ReadAllLines(path).Skip(1).Select(line => line.Split('\t'))
            .Where(row => row[1] == "SC-12").ToArray();
        Assert.Equal(118, rows.Length);
        Assert.Equal(110, rows.Count(row => row[2] == "covered"));
        Assert.Equal(8, rows.Count(row => row[2] == "not_applicable"));
        Assert.All(rows.Where(row => row[2] == "not_applicable"), row =>
            Assert.StartsWith("Subquery_OptionHints_", row[0]));
        foreach (var row in rows.Where(row => row[2] == "covered"))
        {
            var key = row[3].Split(':')[0];
            if (!RuleCatalog.Default.Definitions.ContainsKey(key))
                key = key.Substring(0, key.LastIndexOf('.'));
            Assert.True(RuleCatalog.Default.Definitions.ContainsKey(key), row[0]);
        }
    }

    [Fact]
    public void Nested_clause_overrides_round_trip_in_json_v2_and_v1_keeps_defaults()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var json = """{"version":2,"rules":{"subquery.useSelectFormatting":false,"subquery.where.breakAfter":"always","subquery.join.keywordIndent":{"enabled":true,"offset":-1,"onNewLineOnly":false,"style":"absolute","transparent":false}}}""";
        var options = serializer.Deserialize(json);
        var roundTrip = serializer.Deserialize(serializer.Serialize(options));
        Assert.Equal("always", roundTrip.Rules.Get("subquery.where.breakAfter").Choice);
        Assert.Equal(-1, roundTrip.Rules.Get("subquery.join.keywordIndent").Indent.Offset);
        Assert.False(roundTrip.Rules.Get("subquery.useSelectFormatting").Boolean);
        var v1 = serializer.Deserialize("""{"version":1,"where":{"conditionNewLine":false}}""");
        Assert.True(v1.Rules.Get("subquery.useSelectFormatting").Boolean);
        Assert.Equal("inherit", v1.Rules.Get("subquery.where.breakAfter").Choice);
    }
    private static string Format(string sql, params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default)
            .With("subquery.useSelectFormatting", RuleValue.FromBoolean(false));
        foreach (var (key, value) in rules) values = values.With(key, value);
        var options = FormattingOptions.Default.With(rules: values);
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic =>
            diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        return first.Text;
    }

    private static (string, RuleValue) Choice(string key, string choice) =>
        (key, RuleValue.FromChoice(choice));
    private static (string, RuleValue) Indent(string key, int offset,
        bool onNewLineOnly = true, string style = "relative", bool transparent = false) =>
        (key, RuleValue.FromIndent(new IndentRule(true, offset, onNewLineOnly, style, transparent)));

    [Fact]
    public void From_and_join_rules_target_only_the_nested_query()
    {
        const string sql = "SELECT d.a FROM dbo.OuterTable o JOIN (SELECT a FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id AND a.K=b.K) d ON o.a=d.a";
        var nested = Format(sql, Choice("subquery.from.breakAfter", "always"),
            Choice("subquery.join.breakAfter", "always"),
            Choice("subquery.join.onBreakAfter", "always"));
        Assert.Contains("FROM\n", nested);
        Assert.Contains("JOIN\n", nested);
        Assert.Contains("ON\n", nested);
        Assert.DoesNotContain("JOIN\n(", nested);
    }

    [Theory]
    [InlineData("where", "SELECT x FROM (SELECT a AS x FROM dbo.T WHERE a=1 AND b=2) d", "WHERE")]
    [InlineData("having", "SELECT x FROM (SELECT a AS x FROM dbo.T GROUP BY a HAVING COUNT(*)>1 AND MAX(b)>2) d", "HAVING")]
    [InlineData("groupBy", "SELECT x FROM (SELECT a AS x FROM dbo.T GROUP BY a, b) d", "GROUP BY")]
    [InlineData("orderBy", "SELECT x FROM (SELECT TOP (100) PERCENT a AS x FROM dbo.T ORDER BY a, b) d", "ORDER BY")]
    public void Nested_clauses_have_independent_boundaries(string scope, string sql, string keyword)
    {
        var before = Format(sql, Choice("subquery." + scope + ".breakBefore", "never"));
        var after = Format(sql, Choice("subquery." + scope + ".breakAfter", "always"));
        Assert.NotEqual(before, after);
        Assert.Contains(keyword + "\n", after);
    }

    [Theory]
    [InlineData("where", "SELECT x FROM (SELECT a AS x FROM dbo.T WHERE a=1 AND b=2)")]
    [InlineData("having", "SELECT x FROM (SELECT a AS x FROM dbo.T GROUP BY a HAVING COUNT(*)>1 AND MAX(b)>2)")]
    public void Nested_boolean_wrap_and_indent_are_independent(string scope, string fragment)
    {
        var sql = fragment + " d";
        var before = Format(sql, Choice("subquery." + scope + ".wrapBeforeOperator", "always"));
        var after = Format(sql, Choice("subquery." + scope + ".wrapAfterOperator", "always"));
        var indented = Format(sql, Choice("subquery." + scope + ".wrapAfterOperator", "always"),
            Indent("subquery." + scope + ".nestedConditionIndent", 2));
        Assert.NotEqual(before, after);
        Assert.NotEqual(after, indented);
    }

    [Theory]
    [InlineData("groupBy")]
    [InlineData("orderBy")]
    public void Nested_list_stack_mode_is_observable(string scope)
    {
        var sql = "SELECT x FROM (SELECT " + (scope == "orderBy" ? "TOP (100) PERCENT " : "")
            + "a AS x FROM dbo.T "
            + (scope == "groupBy" ? "GROUP BY" : "ORDER BY") + " a, b) d";
        var stacked = Format(sql, Choice("subquery." + scope + ".stackList", "on"),
            Choice("subquery." + scope + ".stackMode", "onePerLine"));
        var automatic = Format(sql, Choice("subquery." + scope + ".stackList", "on"),
            Choice("subquery." + scope + ".stackMode", "auto"));
        Assert.Contains(",\n", stacked);
        Assert.Contains(", ", automatic);
    }

    [Fact]
    public void Cte_column_list_and_header_have_independent_rules()
    {
        const string sql = "WITH c(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM c";
        var afterWith = Format(sql, Choice("subquery.cte.breakAfterWith", "always"));
        var stacked = Format(sql, Choice("subquery.cte.stackColumns", "on"),
            Choice("subquery.cte.stackMode", "onePerLine"));
        var afterAs = Format(sql, Choice("subquery.cte.breakAfterAs", "always"));
        Assert.Contains("WITH\n", afterWith);
        Assert.Contains(",\n", stacked);
        Assert.Contains("AS\n", afterAs);
    }

    [Theory]
    [InlineData("subquery.cte.breakBeforeColumnOpen", "c\n(")]
    [InlineData("subquery.cte.breakAfterColumnOpen", "(\n")]
    [InlineData("subquery.cte.breakBeforeColumnClose", "b\n)")]
    [InlineData("subquery.cte.breakBeforeAs", ")\nAS")]
    public void Cte_column_braces_and_as_have_separate_breaks(string key, string expected)
    {
        const string sql = "WITH c(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM c";
        Assert.Contains(expected, Format(sql, Choice(key, "always")));
    }

    [Fact]
    public void Nested_from_list_can_stack_without_changing_parent_list()
    {
        const string sql = "SELECT x FROM (SELECT a.x FROM dbo.A a, dbo.B b) d";
        var stacked = Format(sql, Choice("subquery.from.stackList", "on"),
            Choice("subquery.from.stackMode", "onePerLine"));
        var automatic = Format(sql, Choice("subquery.from.stackList", "on"),
            Choice("subquery.from.stackMode", "auto"));
        Assert.Contains(",\n", stacked);
        Assert.Contains(", ", automatic);
    }

    [Theory]
    [InlineData("where", "SELECT x FROM (SELECT a AS x FROM dbo.T WHERE a=1 AND b=2) d")]
    [InlineData("having", "SELECT x FROM (SELECT a AS x FROM dbo.T GROUP BY a HAVING COUNT(*)>1 AND MAX(b)>2) d")]
    public void Nested_wrap_condition_mode_is_not_an_alias_for_other_clauses(string scope, string sql)
    {
        var noWrap = Format(sql, Choice("subquery." + scope + ".wrapCondition", "none"));
        var wrapAnd = Format(sql, Choice("subquery." + scope + ".wrapCondition", "and"));
        Assert.NotEqual(noWrap, wrapAnd);
    }

    [Fact]
    public void For_xml_inside_scalar_subquery_uses_its_own_policy()
    {
        const string sql = "SELECT (SELECT a FROM dbo.T FOR XML PATH('x')) AS x";
        var baseline = Format(sql);
        var changed = Format(sql, Choice("subquery.for.breakAfterXml", "always"));
        Assert.NotEqual(baseline, changed);
        Assert.Contains("XML\n", changed);
        Assert.Contains("'x'", changed);
    }

    [Theory]
    [InlineData("subquery.from.keywordIndent", "subquery.from.breakBefore", "SELECT x FROM (SELECT a AS x FROM dbo.T) d")]
    [InlineData("subquery.from.listIndent", "subquery.from.breakAfter", "SELECT x FROM (SELECT a AS x FROM dbo.T) d")]
    [InlineData("subquery.join.keywordIndent", "subquery.join.breakBefore", "SELECT x FROM (SELECT a.x FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id) d")]
    [InlineData("subquery.join.tableIndent", "subquery.join.breakAfter", "SELECT x FROM (SELECT a.x FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id) d")]
    [InlineData("subquery.join.onKeywordIndent", "subquery.join.onBreakBefore", "SELECT x FROM (SELECT a.x FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id) d")]
    [InlineData("subquery.join.onConditionIndent", "subquery.join.onBreakAfter", "SELECT x FROM (SELECT a.x FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id) d")]
    [InlineData("subquery.where.keywordIndent", "subquery.where.breakBefore", "SELECT x FROM (SELECT a AS x FROM dbo.T WHERE a=1) d")]
    [InlineData("subquery.where.conditionIndent", "subquery.where.breakAfter", "SELECT x FROM (SELECT a AS x FROM dbo.T WHERE a=1) d")]
    [InlineData("subquery.having.keywordIndent", "subquery.having.breakBefore", "SELECT x FROM (SELECT a AS x FROM dbo.T GROUP BY a HAVING COUNT(*)>1) d")]
    [InlineData("subquery.having.conditionIndent", "subquery.having.breakAfter", "SELECT x FROM (SELECT a AS x FROM dbo.T GROUP BY a HAVING COUNT(*)>1) d")]
    [InlineData("subquery.groupBy.keywordIndent", "subquery.groupBy.breakBefore", "SELECT x FROM (SELECT a AS x FROM dbo.T GROUP BY a) d")]
    [InlineData("subquery.groupBy.listIndent", "subquery.groupBy.breakAfter", "SELECT x FROM (SELECT a AS x FROM dbo.T GROUP BY a) d")]
    [InlineData("subquery.orderBy.keywordIndent", "subquery.orderBy.breakBefore", "SELECT x FROM (SELECT TOP (100) PERCENT a AS x FROM dbo.T ORDER BY a) d")]
    [InlineData("subquery.orderBy.listIndent", "subquery.orderBy.breakAfter", "SELECT x FROM (SELECT TOP (100) PERCENT a AS x FROM dbo.T ORDER BY a) d")]
    [InlineData("subquery.for.keywordIndent", "subquery.for.breakBefore", "SELECT (SELECT a FROM dbo.T FOR XML PATH('x')) AS x")]
    [InlineData("subquery.for.specIndent", "subquery.for.breakAfterXml", "SELECT (SELECT a FROM dbo.T FOR XML PATH('x')) AS x")]
    public void Nested_clause_indent_changes_boundary_and_inline_policy(string indentKey,
        string breakKey, string sql)
    {
        var baseline = Format(sql, Choice(breakKey, "always"));
        var indented = Format(sql, Choice(breakKey, "always"), Indent(indentKey, 2));
        var lineOnly = Format(sql, Choice(breakKey, "never"), Indent(indentKey, 1));
        var alsoInline = Format(sql, Choice(breakKey, "never"),
            Indent(indentKey, 1, onNewLineOnly: false));
        Assert.NotEqual(baseline, indented);
        Assert.NotEqual(lineOnly, alsoInline);
    }

    [Fact]
    public void Nested_join_style_transparency_and_boolean_modes_are_observable()
    {
        const string sql = "SELECT x FROM (SELECT a.x FROM dbo.A a JOIN dbo.B b ON a.Id=b.Id AND a.K=b.K) d";
        var relative = Format(sql, Choice("subquery.from.breakAfter", "always"),
            Indent("subquery.from.listIndent", 1), Choice("subquery.join.breakBefore", "always"),
            Indent("subquery.join.keywordIndent", 2));
        var absolute = Format(sql, Choice("subquery.from.breakAfter", "always"),
            Indent("subquery.from.listIndent", 1), Choice("subquery.join.breakBefore", "always"),
            Indent("subquery.join.keywordIndent", 2, style: "absolute"));
        var transparent = Format(sql, Choice("subquery.from.breakAfter", "always"),
            Indent("subquery.from.listIndent", 1), Choice("subquery.join.breakBefore", "always"),
            Indent("subquery.join.keywordIndent", 2, transparent: true));
        var before = Format(sql, Choice("subquery.join.wrapBeforeOperator", "always"));
        var after = Format(sql, Choice("subquery.join.wrapAfterOperator", "always"));
        var nestedIndent = Format(sql, Choice("subquery.join.wrapAfterOperator", "always"),
            Indent("subquery.join.nestedConditionIndent", 2));
        Assert.NotEqual(relative, absolute);
        Assert.NotEqual(relative, transparent);
        Assert.NotEqual(before, after);
        Assert.NotEqual(after, nestedIndent);
    }

    [Theory]
    [InlineData("subquery.cte.columnListIndent", "subquery.cte.breakAfterColumnOpen")]
    [InlineData("subquery.cte.columnBraceIndent", "subquery.cte.breakBeforeColumnOpen")]
    [InlineData("subquery.cte.expressionIndent", "subquery.cte.breakAfterWith")]
    [InlineData("subquery.cte.subqueryBraceIndent", "subquery.cte.breakAfterAs")]
    public void Cte_indent_rules_change_their_own_boundaries(string indentKey, string breakKey)
    {
        const string sql = "WITH c(a,b) AS (SELECT a,b FROM dbo.T) SELECT a FROM c";
        var baseline = Format(sql, Choice(breakKey, "always"));
        var indented = Format(sql, Choice(breakKey, "always"), Indent(indentKey, 2));
        var lineOnly = Format(sql, Choice(breakKey, "never"), Indent(indentKey, 1));
        var alsoInline = Format(sql, Choice(breakKey, "never"),
            Indent(indentKey, 1, onNewLineOnly: false));
        Assert.NotEqual(baseline, indented);
        Assert.NotEqual(lineOnly, alsoInline);
    }

    [Fact]
    public void Inner_overrides_do_not_leak_to_outer_select_or_another_profile()
    {
        const string sql = "SELECT d.a FROM (SELECT a FROM dbo.I WHERE a=1) d";
        var inner = Format(sql, Choice("subquery.from.breakAfter", "always"));
        var outer = Format(sql, Choice("select.from.breakAfter", "always"));
        Assert.Contains("FROM\n", inner);
        Assert.NotEqual(inner, outer);
        Assert.Contains("FROM\n", outer);
    }

    [Fact]
    public void Inheritance_switch_changes_clause_override_source()
    {
        const string sql = "SELECT x FROM (SELECT a AS x FROM dbo.T WHERE a=1) d";
        var rules = new RuleOptions(RuleCatalog.Default)
            .With("select.where.breakAfter", RuleValue.FromChoice("always"))
            .With("subquery.where.breakAfter", RuleValue.FromChoice("never"));
        var formatter = new ScriptDomSqlFormatter();
        var inherited = formatter.Format(sql, FormattingOptions.Default.With(rules: rules),
            new FormatRequest());
        var independent = formatter.Format(sql, FormattingOptions.Default.With(rules: rules.With(
            "subquery.useSelectFormatting", RuleValue.FromBoolean(false))), new FormatRequest());
        Assert.True(inherited.ParseSucceeded);
        Assert.True(independent.ParseSucceeded);
        Assert.Contains("WHERE\n", inherited.Text);
        Assert.Contains("WHERE a", independent.Text);
    }

    [Fact]
    public void Two_levels_of_independent_clauses_are_stable()
    {
        const string sql = "SELECT (SELECT (SELECT a FROM dbo.T WHERE a=1) AS x FROM dbo.U WHERE b=2) AS y";
        var text = Format(sql, Choice("subquery.where.breakAfter", "always"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, "WHERE\\n").Count);
    }

    [Fact]
    public void Nested_rules_preserve_comments_literals_and_two_levels()
    {
        const string sql = "SELECT x FROM (SELECT (SELECT 'FROM WHERE' FROM dbo.U) AS x FROM dbo.T -- keep\nWHERE a=1) d";
        var text = Format(sql, Choice("subquery.where.breakAfter", "always"),
            Choice("subquery.from.breakAfter", "always"));
        Assert.Contains("'FROM WHERE'", text);
        Assert.Contains("-- keep", text);
    }

    [Theory]
    [InlineData("SELECT x FROM (SELECT a FROM dbo.T OPTION (RECOMPILE)) d")]
    [InlineData("SELECT (SELECT a FROM dbo.T OPTION (RECOMPILE)) AS x")]
    [InlineData("WITH c AS (SELECT a FROM dbo.T OPTION (RECOMPILE)) SELECT a FROM c")]
    public void Option_hints_inside_subquery_are_not_valid_tsql(string sql)
    {
        var parsed = new ScriptDomSqlParser().Parse(sql, SqlDialectVersion.Auto);
        Assert.False(parsed.ParseSucceeded);
    }

    [Fact]
    public void Unsupported_subquery_option_rule_is_not_exposed_as_a_no_op()
    {
        Assert.DoesNotContain(RuleCatalog.Default.Definitions.Keys, key =>
            key.StartsWith("subquery.option.", StringComparison.Ordinal));
        var parsed = new SqlFormatterConfigurationSerializer().Parse(
            """{"version":2,"rules":{"subquery.option.breakBefore":"always"}}""");
        Assert.False(parsed.Succeeded);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "TSF2000");
    }
}
