using System.Text.RegularExpressions;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class CaseAndSetParityTests
{
    private const string SearchedCase =
        "SELECT CASE WHEN a=1 AND b=2 THEN 'yes' WHEN c=3 THEN 'maybe' ELSE 'no' END AS result FROM dbo.T";
    private const string SimpleCase =
        "SELECT CASE status WHEN 1 THEN 'one' ELSE 'other' END AS result FROM dbo.T";
    private const string SetQuery =
        "SELECT id FROM dbo.A UNION ALL SELECT id FROM dbo.B EXCEPT SELECT id FROM dbo.C";

    [Fact]
    public void Catalog_and_ledger_resolve_all_sc13_paths()
    {
        var keys = RuleCatalog.Default.Definitions.Keys.Where(key =>
            key.StartsWith("case.", StringComparison.Ordinal)
            || key.StartsWith("setOperator.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(20, keys.Length);
        var path = Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "coverage.tsv");
        var rows = File.ReadAllLines(path).Skip(1).Select(line => line.Split('\t'))
            .Where(row => row[1] == "SC-13").ToArray();
        Assert.Equal(39, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal("covered", row[2]);
            Assert.Contains(row[3], keys);
            Assert.NotEqual("-", row[4]);
            Assert.NotEqual("-", row[5]);
        });
    }

    [Theory]
    [InlineData("case.breakBeforeCase", "never", "SELECT CASE")]
    [InlineData("case.breakBeforeEnd", "never", "'no' END")]
    [InlineData("case.breakBeforeThen", "never", "a=1 AND b=2 THEN")]
    [InlineData("case.breakBeforeWhenElse", "never", "'yes' WHEN")]
    [InlineData("case.breakAfterThenElse", "always", "THEN\n")]
    public void Case_boundaries_are_independent(string key, string mode, string expected)
    {
        var baseline = Format(SearchedCase);
        var changed = Format(SearchedCase, (key, RuleValue.FromChoice(mode)));
        Assert.NotEqual(baseline, changed);
        Assert.Contains(expected, changed);
    }

    [Fact]
    public void Simple_case_input_has_a_separate_boundary()
    {
        var baseline = Format(SimpleCase);
        var changed = Format(SimpleCase,
            ("case.breakBeforeInput", RuleValue.FromChoice("always")));
        Assert.NotEqual(baseline, changed);
        Assert.Contains("CASE\n", changed);
    }

    [Theory]
    [InlineData("case.wrapCondition", "both", "\nAND")]
    [InlineData("case.wrapBeforeOperator", "always", "\nAND")]
    [InlineData("case.wrapAfterOperator", "always", "AND\n")]
    public void Searched_case_boolean_rules_change_only_condition_layout(
        string key, string mode, string expected)
    {
        var changed = Format(SearchedCase, (key, RuleValue.FromChoice(mode)));
        Assert.Contains(expected, Regex.Replace(changed, @"(?m)^[ ]+", ""));
        Assert.Contains("THEN 'yes'", changed);
    }

    [Theory]
    [InlineData("case.caseIndent", null)]
    [InlineData("case.codeIndent", null)]
    [InlineData("case.inputIndent", "case.breakBeforeInput")]
    [InlineData("case.thenKeywordIndent", null)]
    [InlineData("case.whenExpressionIndent", null)]
    [InlineData("case.whenKeywordIndent", null)]
    [InlineData("case.nestedConditionIndent", "case.wrapAfterOperator")]
    public void Every_case_indent_targets_a_real_boundary(string key, string? enablingBreak)
    {
        var source = key == "case.inputIndent" ? SimpleCase : SearchedCase;
        var indent = (key, RuleValue.FromIndent(new IndentRule(true, 2, false)));
        var rules = enablingBreak is null ? new[] { indent } : new[]
        {
            indent, (enablingBreak, RuleValue.FromChoice("always"))
        };
        var baseline = enablingBreak is null ? Format(source)
            : Format(source, (enablingBreak, RuleValue.FromChoice("always")));
        Assert.NotEqual(baseline, Format(source, rules));
    }

    [Theory]
    [InlineData("setOperator.breakBefore", "never", "dbo.A UNION ALL")]
    [InlineData("setOperator.breakAfter", "never", "UNION ALL SELECT")]
    public void Set_operator_boundaries_cover_chained_queries(string key, string mode,
        string expected)
    {
        var changed = Format(SetQuery, (key, RuleValue.FromChoice(mode)));
        Assert.Contains(expected, changed);
        Assert.Contains("EXCEPT", changed);
    }

    [Theory]
    [InlineData("setOperator.keywordIndent")]
    [InlineData("setOperator.branchIndent")]
    public void Set_operator_indents_affect_nested_branches(string key)
    {
        const string nested = "SELECT x.id FROM (SELECT id FROM dbo.A UNION SELECT id FROM dbo.B) x";
        var changed = Format(nested, (key, RuleValue.FromIndent(new IndentRule(true, 2))));
        Assert.NotEqual(Format(nested), changed);
        Assert.Contains("UNION", changed);
    }

    [Theory]
    [InlineData("SELECT CASE WHEN a=1 /* keep */ THEN 2 ELSE 3 END FROM dbo.T",
        "case.breakBeforeThen")]
    [InlineData("SELECT id FROM dbo.A /* keep */ UNION SELECT id FROM dbo.B",
        "setOperator.breakBefore")]
    public void Comments_are_preserved_and_unsupported_gaps_are_skipped(string source, string key)
    {
        var changed = Format(source, (key, RuleValue.FromChoice("always")));
        Assert.Contains("/* keep */", changed);
    }

    [Fact]
    public void Nested_cases_and_set_queries_use_the_same_rules_without_changing_literals()
    {
        const string source = "SELECT (SELECT CASE WHEN x=1 THEN CASE WHEN y=2 THEN 'UNION AND' ELSE 'other' END ELSE 'none' END FROM dbo.T) AS v UNION SELECT 'UNION AND'";
        var changed = Format(source,
            ("case.breakBeforeThen", RuleValue.FromChoice("never")),
            ("setOperator.breakAfter", RuleValue.FromChoice("never")));
        Assert.Contains("WHEN x=1 THEN", changed);
        Assert.Contains("WHEN y=2 THEN", changed);
        Assert.Contains("UNION SELECT 'UNION AND'", changed);
        Assert.Equal(2, Regex.Matches(changed, "'UNION AND'").Count);
    }

    [Fact]
    public void V2_rules_round_trip_and_v1_retains_inherited_defaults()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var settings = serializer.Deserialize("""{"version":2,"rules":{"case.breakBeforeThen":"never","case.codeIndent":{"enabled":true,"offset":2,"onNewLineOnly":false,"style":"relative","transparent":false},"setOperator.breakAfter":"never"}}""");
        var roundTrip = serializer.Deserialize(serializer.Serialize(settings));
        Assert.Equal("never", roundTrip.Rules.Get("case.breakBeforeThen").Choice);
        Assert.Equal(2, roundTrip.Rules.Get("case.codeIndent").Indent.Offset);
        Assert.Equal("never", roundTrip.Rules.Get("setOperator.breakAfter").Choice);
        var v1 = serializer.Deserialize("""{"version":1}""");
        Assert.Equal("inherit", v1.Rules.Get("case.breakBeforeThen").Choice);
        Assert.Equal("inherit", v1.Rules.Get("setOperator.breakAfter").Choice);
    }

    private static string Format(string source, params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        var options = FormattingOptions.Default.With(rules: values);
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(source, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic =>
            diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        var parser = new ScriptDomSqlParser();
        var before = parser.Parse(source, SqlDialectVersion.Auto, CancellationToken.None);
        var after = parser.Parse(first.Text, SqlDialectVersion.Auto, CancellationToken.None);
        Assert.True(before.ParseSucceeded);
        Assert.True(after.ParseSucceeded);
        Assert.Equal(before.Tokens.Where(token => token.TokenType is not
                (Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.WhiteSpace
                    or Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.EndOfFile))
                .Select(token => token.Text),
            after.Tokens.Where(token => token.TokenType is not
                (Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.WhiteSpace
                    or Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.EndOfFile))
                .Select(token => token.Text));
        return first.Text;
    }
}
