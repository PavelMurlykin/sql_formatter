using Newtonsoft.Json.Linq;
using TSqlFormatter.Configuration;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlEnvironmentTests
{
    [Fact]
    public void Empty_or_unavailable_context_is_unknown_never_implicitly_dev()
    {
        var context = new SqlEnvironmentContext("query.sql");
        Assert.Equal(SqlEnvironmentKind.Unknown, SqlEnvironmentConfiguration.Default.Match(context).Kind);
        var rules = new SqlEnvironmentConfiguration(new[] { new SqlEnvironmentRule(SqlEnvironmentKind.Dev, "dev") });
        Assert.Equal(SqlEnvironmentKind.Unknown, rules.Match(context).Kind);
        Assert.Equal("#6B7280", rules.Match(context).Color);
    }

    [Fact]
    public void Nonempty_fields_are_combined_with_and_and_match_literal_substrings_without_case()
    {
        var config = new SqlEnvironmentConfiguration(new[] {
            new SqlEnvironmentRule(SqlEnvironmentKind.Test, " SQL-TEST ", " Shop ", @"\Queries\", "#a1b2c3") });
        var match = config.Match(new(@"C:\Queries\q.sql", "sql-test-01", "shop_copy"));
        Assert.Equal(SqlEnvironmentKind.Test, match.Kind); Assert.Equal("TEST", match.Label); Assert.Equal("#A1B2C3", match.Color);
        Assert.Equal(SqlEnvironmentKind.Unknown, config.Match(new(@"C:\Queries\q.sql", "other", "shop")).Kind);
        Assert.Equal(SqlEnvironmentKind.Unknown, config.Match(new(@"C:\Queries\q.sql", "sql-test-01", null)).Kind);
        Assert.Equal(SqlEnvironmentKind.Unknown, config.Match(new(@"C:\Other\q.sql", "sql-test-01", "shop")).Kind);
        var literal = new SqlEnvironmentConfiguration(new[] { new SqlEnvironmentRule(SqlEnvironmentKind.Prod, "prod*") });
        Assert.Equal(SqlEnvironmentKind.Unknown, literal.Match(new("q.sql", "prod01")).Kind);
    }

    [Fact]
    public void Document_rules_work_offline_but_server_rules_do_not_guess_from_filenames()
    {
        var context = new SqlEnvironmentContext(@"C:\work\prod\q.sql");
        Assert.Equal(SqlEnvironmentKind.Unknown, new SqlEnvironmentConfiguration(new[] {
            new SqlEnvironmentRule(SqlEnvironmentKind.Prod, "prod") }).Match(context).Kind);
        Assert.Equal(SqlEnvironmentKind.Prod, new SqlEnvironmentConfiguration(new[] {
            new SqlEnvironmentRule(SqlEnvironmentKind.Prod, documentContains: @"\prod\") }).Match(context).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Production_wins_overlap_regardless_of_rule_order(bool reverse)
    {
        var rules = new[] { new SqlEnvironmentRule(SqlEnvironmentKind.Dev, "server"),
            new SqlEnvironmentRule(SqlEnvironmentKind.Test, "server"), new SqlEnvironmentRule(SqlEnvironmentKind.Prod, "server") };
        var config = new SqlEnvironmentConfiguration(reverse ? rules.Reverse() : rules);
        Assert.Equal(SqlEnvironmentKind.Prod, config.Match(new("q.sql", "server")).Kind);
        Assert.Equal(SqlEnvironmentKind.Test, new SqlEnvironmentConfiguration(rules.Take(2)).Match(new("q.sql", "server")).Kind);
    }

    [Fact]
    public void Rule_order_breaks_same_environment_ties_and_configuration_owns_a_copy()
    {
        var rules = new[] { new SqlEnvironmentRule(SqlEnvironmentKind.Prod, "p", color: "#112233"),
            new SqlEnvironmentRule(SqlEnvironmentKind.Prod, "p", color: "#445566") };
        var config = new SqlEnvironmentConfiguration(rules);
        rules[0] = new SqlEnvironmentRule(SqlEnvironmentKind.Dev, "p");
        Assert.Equal("#112233", config.Match(new("q.sql", "p")).Color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#123")]
    [InlineData("#GG0000")]
    [InlineData("#12345678")]
    public void Invalid_colors_are_rejected(string color) => Assert.Throws<ArgumentException>(() =>
        new SqlEnvironmentRule(SqlEnvironmentKind.Prod, "prod", color: color));

    [Fact]
    public void Empty_catchall_unknown_kind_and_excessive_limits_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SqlEnvironmentRule(SqlEnvironmentKind.Prod, " ", "\t", ""));
        Assert.Throws<ArgumentException>(() => new SqlEnvironmentRule(SqlEnvironmentKind.Unknown, "prod"));
        Assert.Throws<ArgumentException>(() => new SqlEnvironmentRule((SqlEnvironmentKind)99, "prod"));
        Assert.Throws<ArgumentException>(() => new SqlEnvironmentRule(SqlEnvironmentKind.Dev, new string('x', 1025)));
        var rule = new SqlEnvironmentRule(SqlEnvironmentKind.Dev, "dev");
        Assert.Equal(100, new SqlEnvironmentConfiguration(Enumerable.Repeat(rule, 100)).Rules.Count);
        Assert.Throws<ArgumentException>(() => new SqlEnvironmentConfiguration(Enumerable.Repeat(rule, 101)));
        Assert.Throws<ArgumentException>(() => new SqlEnvironmentConfiguration(new SqlEnvironmentRule[] { null! }));
    }

    [Fact]
    public void Editor_settings_round_trip_environment_rules_without_changing_analysis_or_history()
    {
        var original = new EditorFeaturesSettings(historyEnabled: false, historyMaxAgeDays: 14,
            environments: new SqlEnvironmentConfiguration(new[] { new SqlEnvironmentRule(SqlEnvironmentKind.Prod,
                "prod\\instance", "Accounting", @"\migrations\", "#902020") }));
        var restored = EditorFeaturesSettings.Deserialize(original.Serialize());
        var rule = Assert.Single(restored.Environments.Rules);
        Assert.Equal(SqlEnvironmentKind.Prod, rule.Kind); Assert.Equal("prod\\instance", rule.ServerContains);
        Assert.Equal("Accounting", rule.DatabaseContains); Assert.Equal(@"\migrations\", rule.DocumentContains);
        Assert.Equal("#902020", rule.Color); Assert.False(restored.HistoryEnabled); Assert.Equal(14, restored.HistoryMaxAgeDays);
        Assert.True(restored.Analysis.Enabled);
        var legacy = JObject.Parse(original.Serialize()); legacy.Remove("environments");
        Assert.Empty(EditorFeaturesSettings.Deserialize(legacy.ToString()).Environments.Rules);
        Assert.Empty(EditorFeaturesSettings.Deserialize("", false).Environments.Rules);
    }

    [Theory]
    [InlineData("{}")] [InlineData("null")] [InlineData("[null]")]
    [InlineData("[{\"environment\":\"1\",\"serverContains\":\"prod\"}]")]
    [InlineData("[{\"environment\":\"PROD\",\"serverContains\":5}]")]
    [InlineData("[{\"environment\":\"PROD\",\"serverContains\":\"prod\",\"DatabaseContains\":\"db\"}]")]
    [InlineData("[{\"environment\":\"PROD\",\"serverContains\":\"prod\",\"color\":null}]")]
    public void Malformed_saved_environment_rules_are_not_silently_ignored(string rules)
    {
        var saved = JObject.Parse(new EditorFeaturesSettings().Serialize()); saved["environments"] = JToken.Parse(rules);
        Assert.Throws<ArgumentException>(() => EditorFeaturesSettings.Deserialize(saved.ToString()));
    }

    [Fact]
    public void Context_change_detection_includes_server_database_and_document()
    {
        var original = new SqlEnvironmentContext("q.sql", "server", "db");
        Assert.True(original.SameAs(new("Q.SQL", "SERVER", "DB")));
        Assert.False(original.SameAs(new("q.sql", "server", "other")));
        Assert.False(original.SameAs(new("q.sql", "other", "db")));
        Assert.False(original.SameAs(new("other.sql", "server", "db")));
    }
}
