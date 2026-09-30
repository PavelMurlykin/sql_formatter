using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SubqueryParityTests
{
    [Fact]
    public void Catalog_exposes_all_thirty_sc11_rules()
    {
        Assert.Equal(30, RuleCatalog.Default.Definitions.Keys.Count(key =>
            key.StartsWith("subquery.", StringComparison.Ordinal)));
    }

    [Fact]
    public void All_forty_four_sc11_profile_paths_are_covered_by_native_rules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "coverage.tsv");
        var rows = File.ReadAllLines(path).Skip(1).Select(line => line.Split('\t'))
            .Where(row => row[1] == "SC-11").ToArray();
        Assert.Equal(44, rows.Length);
        foreach (var row in rows)
        {
            Assert.Equal("covered", row[2]);
            var key = row[3].Split(':')[0];
            if (!RuleCatalog.Default.Definitions.ContainsKey(key))
                key = key.Substring(0, key.LastIndexOf('.'));
            Assert.True(RuleCatalog.Default.Definitions.ContainsKey(key), row[0]);
        }
    }

    private static string Format(string sql, params (string Key, RuleValue Value)[] rules) =>
        FormatWidth(sql, 100, rules);

    private static string FormatWidth(string sql, int width, params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        var options = FormattingOptions.Default.With(rules: values,
            general: new GeneralOptions(maxLineWidth: width));
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        var second = formatter.Format(first.Text, options, new FormatRequest());
        Assert.Equal(first.Text, second.Text);
        return first.Text;
    }

    private static (string, RuleValue) Choice(string key, string value) =>
        (key, RuleValue.FromChoice(value));
    private static (string, RuleValue) Boolean(string key, bool value) =>
        (key, RuleValue.FromBoolean(value));
    private static (string, RuleValue) Threshold(string key, bool enabled, int value) =>
        (key, RuleValue.FromThreshold(new ThresholdRule(enabled, value)));

    [Fact]
    public void Select_list_rules_are_inherited_or_isolated_by_switch()
    {
        const string sql = "SELECT x FROM (SELECT a, b FROM dbo.T) d";
        var inherited = Format(sql, Choice("select.list.stackColumns", "on"),
            Choice("select.list.stackMode", "onePerLine"));
        var independent = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("select.list.stackColumns", "on"), Choice("select.list.stackMode", "onePerLine"));
        var nestedStacked = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.list.stackColumns", "on"),
            Choice("subquery.list.stackMode", "onePerLine"));
        Assert.Contains("a,\n", inherited);
        Assert.Contains("SELECT a, b", independent);
        Assert.Contains("a,\n", nestedStacked);
    }

    [Fact]
    public void Independent_list_break_and_indent_are_applied()
    {
        const string sql = "SELECT x FROM (SELECT a, b FROM dbo.T) d";
        var beforeFirst = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.list.breakBeforeFirstColumn", "always"));
        var indented = Format(sql, Boolean("subquery.useSelectFormatting", false),
            ("subquery.list.indent", RuleValue.FromIndent(new IndentRule(true, 2, true))),
            Choice("subquery.list.breakBeforeFirstColumn", "always"));
        Assert.Contains("SELECT\n", beforeFirst);
        Assert.NotEqual(beforeFirst, indented);
        var inlineOnlyNewline = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.list.breakBeforeFirstColumn", "never"),
            ("subquery.list.indent", RuleValue.FromIndent(new IndentRule(true, 1, true))));
        var inlineAlways = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.list.breakBeforeFirstColumn", "never"),
            ("subquery.list.indent", RuleValue.FromIndent(new IndentRule(true, 1, false))));
        Assert.NotEqual(inlineOnlyNewline, inlineAlways);
    }

    [Fact]
    public void Independent_brace_breaks_and_indent_are_applied()
    {
        const string sql = "SELECT x FROM (SELECT a FROM dbo.T) d";
        var beforeOpen = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakBeforeOpen", "always"));
        var afterOpen = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakAfterOpen", "never"));
        var beforeClose = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakBeforeClose", "never"));
        var afterClose = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakAfterClose", "always"));
        var indented = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakAfterOpen", "always"),
            ("subquery.indent", RuleValue.FromIndent(new IndentRule(true, 2, true))));
        Assert.Contains("FROM\n(", beforeOpen);
        Assert.Contains("( SELECT", afterOpen);
        Assert.Contains("T )", beforeClose);
        Assert.Contains(")\nd", afterClose);
        Assert.NotEqual(Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakAfterOpen", "always")), indented);
        Assert.Contains("\n        SELECT a\n            FROM dbo.T", indented);
        var inlineNewlineOnly = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakAfterOpen", "never"),
            ("subquery.indent", RuleValue.FromIndent(new IndentRule(true, 2, true))));
        var inlineAlways = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakAfterOpen", "never"),
            ("subquery.indent", RuleValue.FromIndent(new IndentRule(true, 2, false))));
        Assert.Contains("( SELECT", inlineNewlineOnly);
        Assert.Contains("(         SELECT", inlineAlways);
    }

    [Theory]
    [InlineData("allAnySomeExists", "SELECT a FROM dbo.T WHERE EXISTS (SELECT 1 FROM dbo.U)")]
    [InlineData("cteQueries", "WITH c AS (SELECT a FROM dbo.T) SELECT a FROM c")]
    [InlineData("fromList", "SELECT x FROM (SELECT a AS x FROM dbo.T) d")]
    [InlineData("inOperator", "SELECT a FROM dbo.T WHERE a IN (SELECT a FROM dbo.U)")]
    [InlineData("other", "SELECT (SELECT a FROM dbo.T) AS x")]
    public void Single_line_any_is_scoped_to_its_subquery_category(string category, string sql)
    {
        var baseline = Format(sql, Boolean("subquery.useSelectFormatting", false));
        var compact = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Boolean("subquery.singleLine." + category + ".any", true));
        Assert.Contains("(SELECT", compact);
        Assert.NotEqual(baseline, compact);
    }

    [Theory]
    [InlineData("ALL")]
    [InlineData("ANY")]
    [InlineData("SOME")]
    public void Comparison_quantifiers_share_the_exists_category(string quantifier)
    {
        var sql = "SELECT a FROM dbo.T WHERE a = " + quantifier + " (SELECT a FROM dbo.U)";
        var compact = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Boolean("subquery.singleLine.allAnySomeExists.any", true));
        Assert.Contains("(SELECT", compact);
    }

    [Theory]
    [InlineData("allAnySomeExists", "SELECT a FROM dbo.T WHERE EXISTS (SELECT 1 FROM dbo.U)")]
    [InlineData("cteQueries", "WITH c AS (SELECT a FROM dbo.T) SELECT a FROM c")]
    [InlineData("fromList", "SELECT x FROM (SELECT a AS x FROM dbo.T) d")]
    [InlineData("inOperator", "SELECT a FROM dbo.T WHERE a IN (SELECT a FROM dbo.U)")]
    [InlineData("other", "SELECT (SELECT a FROM dbo.T) AS x")]
    public void Single_line_thresholds_and_margin_limit_compaction(string category, string sql)
    {
        var key = "subquery.singleLine." + category + ".";
        var options = Boolean("subquery.useSelectFormatting", false);
        var shortWords = Format(sql, options, Threshold(key + "maxWords", true, 1));
        var enoughWords = Format(sql, options, Threshold(key + "maxWords", true, 100));
        var shortChars = Format(sql, options, Threshold(key + "maxCharacters", true, 5));
        var enoughChars = Format(sql, options, Threshold(key + "maxCharacters", true, 200));
        var fitsMargin = Format(sql, options, Boolean(key + "whenFitsMargin", true));
        var missesMargin = FormatWidth(sql, 12, options, Boolean(key + "whenFitsMargin", true));
        var anyAtNarrowMargin = FormatWidth(sql, 12, options, Boolean(key + "any", true));
        Assert.DoesNotContain("(SELECT", shortWords);
        Assert.Contains("(SELECT", enoughWords);
        Assert.DoesNotContain("(SELECT", shortChars);
        Assert.Contains("(SELECT", enoughChars);
        Assert.Contains("(SELECT", fitsMargin);
        Assert.DoesNotContain("(SELECT", missesMargin);
        Assert.Contains("(SELECT", anyAtNarrowMargin);
    }

    [Fact]
    public void Single_line_never_flattens_comment_or_multiline_literal()
    {
        var sql = "SELECT a FROM dbo.T WHERE a IN (SELECT a /*keep*/ FROM dbo.U)";
        var formatted = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Boolean("subquery.singleLine.inOperator.any", true));
        Assert.Contains("/*keep*/", formatted);
        Assert.DoesNotContain("(SELECT", formatted);

        const string multilineSql = "SELECT (SELECT 'first\n  second' FROM dbo.T) AS x";
        var rules = new RuleOptions(RuleCatalog.Default)
            .With("subquery.useSelectFormatting", RuleValue.FromBoolean(false))
            .With("subquery.singleLine.other.any", RuleValue.FromBoolean(true));
        var result = new ScriptDomSqlFormatter().Format(multilineSql,
            FormattingOptions.Default.With(rules: rules), new FormatRequest());
        Assert.True(result.ParseSucceeded);
        Assert.False(result.Changed);
        Assert.Equal(multilineSql, result.Text);
    }

    [Fact]
    public void Independent_rules_round_trip_in_json_v2_without_changing_v1_defaults()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var json = """{"version":2,"rules":{"subquery.useSelectFormatting":false,"subquery.list.stackColumns":"on","subquery.singleLine.inOperator.maxWords":{"enabled":true,"value":8}}}""";
        var loaded = serializer.Deserialize(json);
        var again = serializer.Deserialize(serializer.Serialize(loaded));
        Assert.False(again.Rules.Get("subquery.useSelectFormatting").Boolean);
        Assert.Equal("on", again.Rules.Get("subquery.list.stackColumns").Choice);
        Assert.Equal(8, again.Rules.Get("subquery.singleLine.inOperator.maxWords").Threshold.Value);
        var v1 = serializer.Deserialize("""{"version":1,"select":{"columns":"onePerLine"}}""");
        Assert.True(v1.Rules.Get("subquery.useSelectFormatting").Boolean);
    }

    [Fact]
    public void Nested_subquery_rules_preserve_parseability_and_idempotence()
    {
        const string sql = "SELECT (SELECT (SELECT a FROM dbo.T) AS x) AS y";
        var formatted = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Choice("subquery.breakAfterOpen", "always"),
            ("subquery.indent", RuleValue.FromIndent(new IndentRule(true, 2, true))));
        Assert.Contains("SELECT a", formatted);
    }

    [Fact]
    public void Cte_query_brace_is_not_confused_with_cte_column_list_brace()
    {
        const string sql = "WITH c(a, b) AS (SELECT a, b FROM dbo.T) SELECT a FROM c";
        var formatted = Format(sql, Boolean("subquery.useSelectFormatting", false),
            Boolean("subquery.singleLine.cteQueries.any", true));
        Assert.Contains("c (a, b) AS (SELECT", formatted);
    }
}
