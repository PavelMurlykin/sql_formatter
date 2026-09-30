using TSqlFormatter.Configuration;
using TSqlFormatter.Cli;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;
using static TSqlFormatter.Core.Tests.ParityTestSupport;

namespace TSqlFormatter.Core.Tests;

public sealed class MergeBranchParityTests
{
    private const string Sql = "MERGE INTO dbo.T AS t USING dbo.S AS s ON t.id = s.id "
        + "WHEN MATCHED AND (s.a = 1 AND s.b = 2 OR s.b = 3) THEN UPDATE SET t.a = s.a, t.longer = s.b "
        + "WHEN NOT MATCHED BY TARGET AND s.id > 0 THEN INSERT (id, a) VALUES (s.id, 'x  y') "
        + "WHEN NOT MATCHED BY SOURCE THEN DELETE;";

    [Fact]
    public void Catalog_and_ledger_cover_all_branch_paths()
    {
        CheckLedger("SC-17", 64, nameof(MergeBranchParityTests));
        Assert.Equal(40, Ledger("SC-17").Select(row => row[3]).Distinct().Count());
    }

    public static IEnumerable<object[]> Boundaries() => new[]
    {
        ("when.breakBefore", "WHEN"), ("when.breakAfter", "MATCHED"),
        ("when.wrapBeforeOperator", "AND"), ("when.wrapAfterOperator", "s.b"),
        ("then.breakBefore", "THEN"), ("then.breakAfter", "UPDATE"),
        ("update.set.breakBefore", "SET"), ("update.set.breakAfter", "t.a"),
        ("insert.columns.breakBeforeOpen", "("), ("insert.columns.breakAfterOpen", "id"),
        ("insert.columns.breakBeforeClose", ")"), ("insert.values.breakBeforeKeyword", "VALUES"),
        ("insert.values.breakAfterKeyword", "("), ("insert.values.breakAfterOpen", "s.id"),
        ("insert.values.breakBeforeClose", ")")
    }.Select(pair => new object[] { "merge." + pair.Item1, pair.Item2 });

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Every_boundary_has_distinct_always_never_and_inherit_modes(string key, string token)
    {
        var expanded = Format(Sql, Choice(key, "always"));
        Assert.Contains("\n" + token, expanded);
        Assert.NotEqual(expanded, Format(Sql, Choice(key, "never")));
        Assert.Equal(Format(Sql), Format(Sql, Choice(key, "inherit")));
    }

    public static IEnumerable<object[]> Indents() => new[]
    {
        ("when.keywordIndent", "when.breakBefore", "WHEN"), ("when.conditionIndent", "when.breakAfter", "MATCHED"),
        ("when.nestedConditionIndent", "when.wrapAfterOperator", "s.b"),
        ("then.keywordIndent", "then.breakBefore", "THEN"), ("then.actionIndent", "then.breakAfter", "UPDATE"),
        ("update.set.keywordIndent", "update.set.breakBefore", "SET"), ("update.set.listIndent", "update.set.breakAfter", "t.a"),
        ("insert.columns.braceIndent", "insert.columns.breakBeforeOpen", "("),
        ("insert.columns.listIndent", "insert.columns.breakAfterOpen", "id"),
        ("insert.values.keywordIndent", "insert.values.breakBeforeKeyword", "VALUES"),
        ("insert.values.braceIndent", "insert.values.breakAfterKeyword", "("),
        ("insert.values.listIndent", "insert.values.breakAfterOpen", "s.id")
    }.Select(p => new object[] { "merge." + p.Item1, "merge." + p.Item2, p.Item3 });

    [Theory]
    [MemberData(nameof(Indents))]
    public void Every_indent_honors_enabled_offset_newline_style_and_transparency(string key, string boundary, string token)
    {
        var one = Format(Sql, Choice(boundary, "always"), Indent(key));
        Assert.Contains("\n    " + token, one);
        Assert.NotEqual(one, Format(Sql, Choice(boundary, "always"), Indent(key, 2)));
        Assert.Equal(Format(Sql, Choice(boundary, "always")), Format(Sql, Choice(boundary, "always"),
            (key, RuleValue.FromIndent(new IndentRule(false, 2)))));
        Assert.Contains("\n" + token, Format(Sql, Choice(boundary, "always"), Indent(key, 2, transparent: true)));
        Format(Sql, Choice(boundary, "always"), Indent(key, -1, style: "absolute"));
        Assert.Equal(Format(Sql, Choice(boundary, "never")), Format(Sql, Choice(boundary, "never"), Indent(key)));
        Assert.Contains("     " + token, Format(Sql, Choice(boundary, "never"), Indent(key, newlineOnly: false)));
    }

    [Theory]
    [InlineData("insert.columns.spaceBeforeOpen", "INSERT (", "INSERT(")]
    [InlineData("insert.columns.spaceWithin", "( id, a )", "(id, a)")]
    [InlineData("insert.values.spaceAfterKeyword", "VALUES (", "VALUES(")]
    [InlineData("insert.values.spaceWithin", "( s.id, 'x  y' )", "(s.id, 'x  y')")]
    public void Every_space_policy_is_local_and_preserves_literal_contents(string key, string spaced, string tight)
    {
        Assert.Contains(spaced, Format(Sql, Choice("merge." + key, "insert")));
        Assert.Contains(tight, Format(Sql, Choice("merge." + key, "remove")));
    }

    [Theory]
    [InlineData("update.set")]
    [InlineData("insert.columns")]
    [InlineData("insert.values")]
    public void Every_list_supports_on_off_auto_and_leading_commas(string group)
    {
        var prefix = "merge." + group;
        var vertical = Format(Sql, Choice(prefix + ".stackList", "on"));
        var flat = Format(Sql, Choice(prefix + ".stackList", "off"));
        Assert.NotEqual(vertical, flat);
        Assert.Equal(flat, Format(Sql, Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto")));
        Assert.Equal(vertical, Format(Sql, Options(6), Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto")));
        Assert.Contains("\n,", Format(Sql, Choice(prefix + ".stackList", "on"), Choice("stackedList.commaPlacement", "leading")));
    }

    [Fact]
    public void Conditions_inheritance_and_combined_nested_anchors_are_stable()
    {
        var and = Format(Sql, Choice("merge.when.wrapCondition", "and"));
        var or = Format(Sql, Choice("merge.when.wrapCondition", "or"));
        Assert.Contains("\nAND", and);
        Assert.Contains("\nOR", or);
        Assert.NotEqual(and, or);
        Assert.DoesNotContain("\nAND", Format(Sql, Choice("merge.when.wrapCondition", "none")));
        Assert.Contains("\nOR", Format(Sql, Choice("merge.when.wrapCondition", "both")));
        foreach (var action in new[] { "update", "insert" })
        {
            var group = action == "update" ? ".set" : ".columns";
            var shared = Choice(action + group + ".stackList", "on");
            var local = Choice("merge." + action + group + ".stackList", "off");
            Assert.NotEqual(Format(Sql, shared, local), Format(Sql, shared, local,
                ("merge." + action + ".useStatementFormatting", RuleValue.FromBoolean(true))));
        }
        var all = RuleCatalog.Default.Definitions.Values.Where(d => Ledger("SC-17").Any(r => r[3] == d.Key))
            .Where(d => d.DefaultValue.Kind != RuleValueKind.Boolean).Select(d => (d.Key, d.DefaultValue.Kind switch
            {
                RuleValueKind.Indent => RuleValue.FromIndent(new IndentRule(true, 1)),
                _ => RuleValue.FromChoice(d.Choices.Contains("always") ? "always" : d.Choices.Contains("on") ? "on"
                    : d.Choices.Contains("both") ? "both" : d.Choices.Contains("insert") ? "insert" : "auto")
            })).ToArray();
        Format(Sql, all);
        Format("BEGIN " + Sql + " END", all);
        Format(Sql.Replace("WHEN NOT MATCHED BY TARGET", "-- between branches\nWHEN NOT MATCHED BY TARGET"), all);
    }

    [Fact]
    public void V2_round_trips_all_branch_settings_and_default_v1_is_unchanged()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var key in Ledger("SC-17").Select(r => r[3]).Distinct())
            rules = rules.With(key, RuleCatalog.Default.Definitions[key].DefaultValue);
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(Options().With(rules: rules))).Rules.Overrides);
        Assert.Equal(Format(Sql), new ScriptDomSqlFormatter().Format(Sql,
            serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.False(serializer.Parse("""{"version":2,"rules":{"merge.then.breakBefore":"unknown"}}""").Succeeded);
        Assert.Throws<ArgumentException>(() => rules.With("merge.when.keywordIndent", RuleValue.FromIndent(new IndentRule(true, 33))));
    }

    [Fact]
    public void Scoped_formatting_comments_and_unsupported_inputs_remain_safe()
    {
        var formatter = new ScriptDomSqlFormatter();
        var options = Options().With(rules: new RuleOptions(RuleCatalog.Default)
            .With("merge.when.breakBefore", RuleValue.FromChoice("always")));
        var result = formatter.Format(Sql + "\n" + Sql, options,
            new FormatRequest(FormatScope.Statement, new SqlTextSpan(1, 0)));
        Assert.EndsWith("\n" + Sql, result.Text);
        var selection = formatter.Format(Sql + "\n" + Sql, options,
            new FormatRequest(FormatScope.Selection, new SqlTextSpan(0, Sql.Length)));
        Assert.EndsWith("\n" + Sql, selection.Text);
        foreach (var sql in new[] { "MERGE incomplete;", Sql.Replace("'x  y'", "'x\ny'") })
            Assert.Equal(sql, formatter.Format(sql, options, new FormatRequest()).Text);
        Assert.Contains("-- keep WHEN THEN\n", Format(Sql.Replace("THEN UPDATE", "-- keep WHEN THEN\nTHEN UPDATE"),
            Choice("merge.then.breakBefore", "never")));
        var relative = Format(Sql, Choice("merge.when.breakBefore", "always"), Indent("merge.when.keywordIndent"),
            Choice("merge.then.breakBefore", "always"), Indent("merge.then.keywordIndent"));
        Assert.Contains("\n        THEN", relative);
        Assert.Contains("\n    THEN", Format(Sql, Choice("merge.when.breakBefore", "always"), Indent("merge.when.keywordIndent"),
            Choice("merge.then.breakBefore", "always"), Indent("merge.then.keywordIndent", style: "absolute")));
        Assert.NotEqual(Format(Sql, Choice("insert.values.breakAfterOpen", "always")),
            Format(Sql, Choice("insert.values.breakAfterOpen", "always"),
                ("merge.insert.useStatementFormatting", RuleValue.FromBoolean(true))));
    }

    [Fact]
    public async Task Cli_loads_the_documented_branch_configuration()
    {
        const string json = """
        {"version":2,"rules":{
          "merge.when.breakBefore":"always","merge.then.breakBefore":"always","merge.then.breakAfter":"always",
          "merge.then.actionIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
          "merge.update.set.stackList":"on","merge.insert.columns.stackList":"off","merge.insert.values.stackList":"off"
        }}
        """;
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-merge-branches-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, Sql);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(string.Empty, error.ToString());
            var options = new SqlFormatterConfigurationSerializer().Deserialize(json).With(
                keywords: new KeywordOptions(KeywordCase.Preserve));
            Assert.Equal(new ScriptDomSqlFormatter().Format(Sql, options, new FormatRequest()).Text, output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
