using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using static TSqlFormatter.Core.Tests.ParityTestSupport;

namespace TSqlFormatter.Core.Tests;

public sealed class MergeTailParityTests
{
    private const string Sql = "MERGE TOP (10) PERCENT INTO dbo.T AS t USING dbo.S AS s ON t.id = s.id "
        + "WHEN MATCHED THEN DELETE OUTPUT deleted.id, $action INTO @audit(id, action) "
        + "OUTPUT deleted.id, $action OPTION (RECOMPILE, MAXDOP 1);";

    [Fact]
    public void Ledger_resolves_all_109_merge_groups_and_181_scalar_paths()
    {
        CheckLedger("SC-18", 31, nameof(MergeTailParityTests));
        var rows = Ledger("SC-16").Concat(Ledger("SC-17")).Concat(Ledger("SC-18")).ToArray();
        Assert.Equal(181, rows.Length);
        Assert.Equal(109, rows.Select(r => r[0].Split('.')[0]).Distinct().Count());
        Assert.All(rows, row => Assert.Equal("covered", row[2]));
        Assert.Equal(19, Ledger("SC-18").Select(r => r[3]).Distinct().Count());
    }

    public static IEnumerable<object[]> Boundaries() => new[]
    {
        ("top.breakBefore", "TOP"), ("top.breakBeforeOpen", "("), ("top.breakAfterOpen", "10"),
        ("top.breakBeforeClose", ")"), ("top.breakBeforePercent", "PERCENT"),
        ("output.breakBefore", "OUTPUT"), ("output.breakAfter", "deleted.id"),
        ("option.breakBefore", "OPTION"), ("option.breakAfter", "(")
    }.Select(p => new object[] { "merge." + p.Item1, p.Item2 });

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
        ("top.keywordIndent", "top.breakBefore", "TOP"), ("top.percentIndent", "top.breakBeforePercent", "PERCENT"),
        ("output.keywordIndent", "output.breakBefore", "OUTPUT"), ("output.listIndent", "output.breakAfter", "deleted.id"),
        ("option.keywordIndent", "option.breakBefore", "OPTION"), ("option.hintsIndent", "option.breakAfter", "(")
    }.Select(p => new object[] { "merge." + p.Item1, "merge." + p.Item2, p.Item3 });

    [Theory]
    [MemberData(nameof(Indents))]
    public void Every_indent_honors_all_native_fields(string key, string boundary, string token)
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
    [InlineData("top.spaceAfterKeyword", "TOP (", "TOP(")]
    [InlineData("top.spaceWithin", "( 10 )", "(10)")]
    public void Top_spaces_and_breaks_are_local(string key, string spaced, string tight)
    {
        Assert.Contains(spaced, Format(Sql, Choice("merge." + key, "insert")));
        Assert.Contains(tight, Format(Sql, Choice("merge." + key, "remove")));
        Assert.Contains("TOP\n(", Format(Sql, Choice("merge.top.breakBeforeOpen", "always"), Choice("merge.top.spaceAfterKeyword", "remove")));
    }

    [Fact]
    public void Output_lists_support_both_clauses_auto_and_leading_commas()
    {
        var flat = Format(Sql, Choice("merge.output.stackList", "off"));
        var vertical = Format(Sql, Choice("merge.output.stackList", "on"));
        Assert.NotEqual(flat, vertical);
        Assert.Equal(flat, Format(Sql, Choice("merge.output.stackList", "on"), Choice("merge.output.stackMode", "auto")));
        Assert.Equal(vertical, Format(Sql, Options(6), Choice("merge.output.stackList", "on"), Choice("merge.output.stackMode", "auto")));
        Assert.Contains("\n,", Format(Sql, Choice("merge.output.stackList", "on"), Choice("stackedList.commaPlacement", "leading")));
        Assert.Contains("INTO @audit(id, action)", vertical);
    }

    [Fact]
    public void Combined_headers_branches_tails_ctes_and_nested_code_are_stable()
    {
        var rules = new[] { Choice("merge.top.breakBefore", "always"), Indent("merge.top.keywordIndent"),
            Choice("merge.top.breakBeforeOpen", "always"), Choice("merge.top.breakAfterOpen", "always"),
            Choice("merge.top.breakBeforeClose", "always"), Choice("merge.top.breakBeforePercent", "always"),
            Indent("merge.top.percentIndent"), Choice("merge.into.breakBefore", "always"),
            Choice("merge.when.breakBefore", "always"), Choice("merge.then.breakAfter", "always"), Indent("merge.then.actionIndent"),
            Choice("merge.output.breakBefore", "always"), Indent("merge.output.keywordIndent"), Choice("merge.output.breakAfter", "always"),
            Indent("merge.output.listIndent"), Choice("merge.output.stackList", "on"), Choice("merge.output.stackMode", "auto"),
            Choice("merge.option.breakBefore", "always"), Indent("merge.option.keywordIndent"), Choice("merge.option.breakAfter", "always"),
            Indent("merge.option.hintsIndent") };
        Format(Sql, rules);
        Format("WITH s AS (SELECT id FROM dbo.S) " + Sql.Replace("dbo.S AS s", "s"), rules);
        Format("BEGIN " + Sql + " END", rules);
        Format(Sql.Replace("(10)", "(@count + (2 * 3))"), rules);
        Format(Sql.Replace("RECOMPILE, MAXDOP", "RECOMPILE,\nMAXDOP"), rules);
        Format(Sql.Replace("THEN DELETE", "THEN /* action */ DELETE"), rules);
        Format(Sql.Replace("deleted.id, $action", "deleted.id, /* preserve */ $action"), rules);
    }

    [Fact]
    public async Task V2_and_cli_share_the_documented_configuration_without_affecting_v1()
    {
        const string json = """
        {"version":2,"rules":{"merge.top.breakBefore":"always","merge.top.spaceWithin":"remove",
        "merge.output.breakBefore":"always","merge.output.stackList":"on","merge.option.breakBefore":"always"}}
        """;
        var serializer = new SqlFormatterConfigurationSerializer();
        var options = serializer.Deserialize(json);
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var key in Ledger("SC-18").Select(r => r[3]).Distinct())
            rules = rules.With(key, RuleCatalog.Default.Definitions[key].DefaultValue);
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(options.With(rules: rules))).Rules.Overrides);
        Assert.Equal(Format(Sql), new ScriptDomSqlFormatter().Format(Sql,
            serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.False(serializer.Parse("""{"version":2,"rules":{"merge.top.spaceWithin":"unknown"}}""").Succeeded);
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-merge-tail-" + Guid.NewGuid().ToString("N"));
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
            Assert.Equal(new ScriptDomSqlFormatter().Format(Sql, options, new FormatRequest()).Text, output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Auto_lists_remain_stable_at_each_margin_with_spacing_and_indent_policies()
    {
        var branch = Sql.Replace("WHEN MATCHED THEN DELETE", "WHEN NOT MATCHED THEN INSERT (a, b) VALUES (1, 2)");
        foreach (var width in Enumerable.Range(1, 80))
            foreach (var space in new[] { "insert", "remove" })
            {
                Format(branch, Options(width), Choice("merge.insert.columns.stackList", "on"),
                    Choice("merge.insert.columns.stackMode", "auto"), Choice("merge.insert.columns.spaceWithin", space),
                    Choice("merge.insert.columns.breakAfterOpen", "always"), Indent("merge.insert.columns.listIndent"));
                Format(Sql, Options(width), Choice("merge.output.stackList", "on"), Choice("merge.output.stackMode", "auto"),
                    Choice("merge.output.breakAfter", "always"), Indent("merge.output.listIndent", newlineOnly: false));
            }
    }
}
