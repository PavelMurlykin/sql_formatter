using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using static TSqlFormatter.Core.Tests.ParityTestSupport;

namespace TSqlFormatter.Core.Tests;

public sealed class TriggerParityTests
{
    private const string Sql = "CREATE TRIGGER dbo.tr ON dbo.T WITH ENCRYPTION, EXECUTE AS OWNER AFTER INSERT, UPDATE, DELETE AS BEGIN SET @a = 1; END;";
    [Fact]
    public void Ledger_covers_all_trigger_fields()
    { CheckLedger("SC-23", 39, nameof(TriggerParityTests)); Assert.Equal(21, Ledger("SC-23").Select(r => r[3]).Distinct().Count()); }
    public static IEnumerable<object[]> Boundaries()
    {
        foreach (var verb in new[] { "CREATE", "ALTER" })
            foreach (var (key, token) in new[] { ("on.breakBefore", "ON"), ("on.breakAfter", "dbo.T"),
                ("with.breakBefore", "WITH"), ("with.breakAfter", "ENCRYPTION"),
                ("events.breakBefore", "AFTER"), ("events.breakAfter", "INSERT"),
                ("body.breakBeforeAs", "AS"), ("body.breakAfterAs", "BEGIN") })
                yield return new object[] { "trigger." + key, Sql.Replace("CREATE", verb), token };
    }
    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Create_and_alter_boundaries_support_distinct_modes(string key, string sql, string token)
    {
        var expanded = Format(sql, Choice(key, "always"));
        Assert.Contains("\n" + token, expanded);
        Assert.NotEqual(expanded, Format(sql, Choice(key, "never")));
        Assert.Equal(Format(sql), Format(sql, Choice(key, "inherit")));
    }
    public static IEnumerable<object[]> Indents() => new[]
    {
        ("on.keywordIndent", "trigger.on.breakBefore", "ON"), ("on.targetIndent", "trigger.on.breakAfter", "dbo.T"),
        ("with.keywordIndent", "trigger.with.breakBefore", "WITH"), ("with.listIndent", "trigger.with.breakAfter", "ENCRYPTION"),
        ("events.keywordIndent", "trigger.events.breakBefore", "AFTER"), ("events.listIndent", "trigger.events.breakAfter", "INSERT"),
        ("body.asIndent", "trigger.body.breakBeforeAs", "AS"), ("body.keywordIndent", "trigger.body.breakAfterAs", "BEGIN"),
        ("body.codeIndent", "code.breakAfterBegin", "SET")
    }.Select(p => new object[] { "trigger." + p.Item1, p.Item2, p.Item3 });
    [Theory]
    [MemberData(nameof(Indents))]
    public void Every_indent_honors_native_fields(string key, string boundary, string token)
    {
        Assert.Contains("\n    " + token, Format(Sql, Choice(boundary, "always"), Indent(key)));
        Assert.Contains("\n        " + token, Format(Sql, Choice(boundary, "always"), Indent(key, 2)));
        Assert.Equal(Format(Sql, Choice(boundary, "always")), Format(Sql, Choice(boundary, "always"),
            (key, RuleValue.FromIndent(new IndentRule(false, 2)))));
        Assert.Contains("\n" + token, Format(Sql, Choice(boundary, "always"), Indent(key, transparent: true)));
        Format(Sql, Choice(boundary, "always"), Indent(key, -1, style: "absolute"));
        Assert.NotEqual(Format(Sql, Choice(boundary, "never")), Format(Sql, Choice(boundary, "never"), Indent(key, newlineOnly: false)));
    }
    [Theory]
    [InlineData("events")]
    [InlineData("with")]
    public void Lists_support_on_off_auto_and_leading_commas(string group)
    {
        var prefix = "trigger." + group;
        var flat = Format(Sql, Choice(prefix + ".stackList", "off"));
        var vertical = Format(Sql, Choice(prefix + ".stackList", "on"));
        Assert.NotEqual(flat, vertical);
        Assert.Equal(flat, Format(Sql, Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto")));
        Assert.Equal(vertical, Format(Sql, Options(6), Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto")));
        Assert.Contains("\n,", Format(Sql, Choice(prefix + ".stackList", "on"), Choice("stackedList.commaPlacement", "leading")));
    }
    [Fact]
    public void Dml_ddl_logon_external_bodies_comments_and_combined_policies_are_safe()
    {
        var rules = Ledger("SC-23").Select(r => r[3]).Distinct().Select(key => (key,
            RuleCatalog.Default.Definitions[key].DefaultValue.Kind == RuleValueKind.Indent
                ? RuleValue.FromIndent(new IndentRule(true, 1)) : RuleValue.FromChoice(
                    RuleCatalog.Default.Definitions[key].Choices.Contains("always") ? "always"
                    : RuleCatalog.Default.Definitions[key].Choices.Contains("on") ? "on" : "auto")))
            .Concat(new[] { Choice("code.breakAfterBegin", "always"), Choice("code.breakBeforeEnd", "always") }).ToArray();
        foreach (var sql in new[] { Sql, Sql.Replace("AFTER", "INSTEAD OF"), Sql.Replace("AFTER", "FOR"),
            "CREATE TRIGGER tr ON DATABASE WITH ENCRYPTION, EXECUTE AS 'dbo' AFTER CREATE_TABLE, ALTER_TABLE AS BEGIN PRINT 1; END;",
            "CREATE TRIGGER tr ON ALL SERVER WITH EXECUTE AS 'sa' FOR LOGON AS PRINT 1;",
            Sql.Replace("AS BEGIN SET @a = 1; END;", "AS EXTERNAL NAME a.b.c;"),
            Sql.Replace("INSERT, UPDATE", "INSERT, /* keep */ UPDATE"), Sql.Replace("AS BEGIN", "AS -- keep\nBEGIN") })
        {
            Format(sql, rules);
            Format(sql.Replace("CREATE TRIGGER", "ALTER TRIGGER"), rules);
            Format(sql.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER"), rules);
        }
        Assert.Contains("\n    ALL SERVER", Format("CREATE TRIGGER tr ON ALL SERVER FOR LOGON AS PRINT 1;",
            Choice("trigger.on.breakAfter", "always"), Indent("trigger.on.targetIndent")));
        Assert.Contains("\n        BEGIN", Format(Sql, Choice("trigger.body.breakBeforeAs", "always"), Indent("trigger.body.asIndent"),
            Choice("trigger.body.breakAfterAs", "always"), Indent("trigger.body.keywordIndent")));
    }
    [Fact]
    public async Task V2_cli_and_safe_fallback_match_the_documented_config()
    {
        const string json = """
        {"version":2,"rules":{"trigger.on.breakBefore":"always","trigger.with.breakBefore":"always",
        "trigger.events.breakBefore":"always","trigger.events.stackList":"on","trigger.body.breakBeforeAs":"always",
        "trigger.body.breakAfterAs":"always","code.breakAfterBegin":"always","code.breakBeforeEnd":"always",
        "trigger.body.codeIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false}}}
        """;
        var serializer = new SqlFormatterConfigurationSerializer();
        var options = serializer.Deserialize(json);
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var key in Ledger("SC-23").Select(r => r[3]).Distinct()) rules = rules.With(key, RuleCatalog.Default.Definitions[key].DefaultValue);
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(options.With(rules: rules))).Rules.Overrides);
        var formatter = new ScriptDomSqlFormatter();
        Assert.Equal(Format(Sql), formatter.Format(Sql, serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        foreach (var sql in new[] { "CREATE TRIGGER broken", Sql.Replace("SET @a = 1", "PRINT 'a\nb'") })
            Assert.Equal(sql, formatter.Format(sql, options, new FormatRequest()).Text);
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-trigger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, Sql);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await TSqlFormatter.Cli.SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(formatter.Format(Sql, options, new FormatRequest()).Text, output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
