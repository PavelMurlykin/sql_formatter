using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using static TSqlFormatter.Core.Tests.ParityTestSupport;

namespace TSqlFormatter.Core.Tests;

public sealed class ModuleParityTests
{
    private const string Proc = "CREATE PROCEDURE dbo.p @a int, @b int WITH RECOMPILE, EXECUTE AS OWNER AS BEGIN SET @a = 1; SELECT @a; END;";
    private const string Function = "CREATE FUNCTION dbo.f(@a int, @b int) RETURNS int WITH SCHEMABINDING, RETURNS NULL ON NULL INPUT AS BEGIN RETURN @a + @b; END;";
    private const string Empty = "CREATE FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1; END;";
    private const string Table = "CREATE FUNCTION dbo.f(@a int) RETURNS @r TABLE (id int, a int) AS BEGIN INSERT INTO @r VALUES (@a, 1); RETURN; END;";
    private const string View = "CREATE VIEW dbo.v(id, a) WITH SCHEMABINDING AS SELECT id, a FROM dbo.T;";

    [Fact]
    public void Ledger_covers_every_routine_and_view_field()
    {
        CheckLedger("SC-21", 67, nameof(ModuleParityTests));
        Assert.Equal(41, Ledger("SC-21").Select(r => r[3]).Distinct().Count());
    }
    public static IEnumerable<object[]> Boundaries()
    {
        var cases = new[]
        {
            ("routine.body.breakBeforeAs", Proc, "AS"), ("routine.body.breakBefore", Proc, "BEGIN"),
            ("routine.parameters.breakBeforeOpen", Function, "("), ("routine.parameters.breakAfterOpen", Function, "@a"),
            ("routine.parameters.breakBeforeClose", Function, ")"), ("routine.returns.breakBefore", Function, "RETURNS"),
            ("routine.returns.breakBeforeTable", Table, "TABLE"), ("routine.with.breakBefore", Proc, "WITH"),
            ("routine.with.breakAfter", Proc, "RECOMPILE"), ("view.columns.breakBeforeOpen", View, "("),
            ("view.columns.breakAfterOpen", View, "id"), ("view.columns.breakBeforeClose", View, ")"),
            ("view.query.breakBeforeAs", View, "AS"), ("view.query.breakAfterAs", View, "SELECT")
        };
        foreach (var verb in new[] { "CREATE", "ALTER" })
            foreach (var (key, sql, token) in cases) yield return new object[] { key, sql.Replace("CREATE", verb), token };
    }
    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Create_and_alter_boundaries_support_all_modes(string key, string sql, string token)
    {
        var expanded = Format(sql, Choice(key, "always"));
        Assert.Contains("\n" + token, expanded);
        Assert.NotEqual(expanded, Format(sql, Choice(key, "never")));
        Assert.Equal(Format(sql), Format(sql, Choice(key, "inherit")));
    }
    public static IEnumerable<object[]> Indents() => new[]
    {
        new object[] { "routine.body.asIndent", "routine.body.breakBeforeAs", Proc, "AS" },
        new object[] { "routine.body.keywordIndent", "routine.body.breakBefore", Proc, "BEGIN" },
        new object[] { "routine.body.codeIndent", "code.breakAfterBegin", Proc, "SET" },
        new object[] { "routine.parameters.listIndent", "routine.parameters.breakAfterOpen", Function, "@a" },
        new object[] { "routine.parameters.braceIndent", "routine.parameters.breakBeforeOpen", Function, "(" },
        new object[] { "routine.returns.tableIndent", "routine.returns.breakBeforeTable", Table, "TABLE" },
        new object[] { "routine.with.keywordIndent", "routine.with.breakBefore", Proc, "WITH" },
        new object[] { "routine.with.listIndent", "routine.with.breakAfter", Proc, "RECOMPILE" },
        new object[] { "view.columns.listIndent", "view.columns.breakAfterOpen", View, "id" },
        new object[] { "view.columns.braceIndent", "view.columns.breakBeforeOpen", View, "(" },
        new object[] { "view.query.asIndent", "view.query.breakBeforeAs", View, "AS" },
        new object[] { "view.query.queryIndent", "view.query.breakAfterAs", View, "SELECT" }
    };
    [Theory]
    [MemberData(nameof(Indents))]
    public void Every_indent_has_enabled_offset_newline_style_and_transparency(string key, string boundary, string sql, string token)
    {
        Assert.Contains("\n    " + token, Format(sql, Choice(boundary, "always"), Indent(key)));
        Assert.Contains("\n        " + token, Format(sql, Choice(boundary, "always"), Indent(key, 2)));
        Assert.Equal(Format(sql, Choice(boundary, "always")), Format(sql, Choice(boundary, "always"),
            (key, RuleValue.FromIndent(new IndentRule(false, 2)))));
        Assert.Contains("\n" + token, Format(sql, Choice(boundary, "always"), Indent(key, transparent: true)));
        Format(sql, Choice(boundary, "always"), Indent(key, -1, style: "absolute"));
        if (key == "routine.body.codeIndent") boundary = "code.breakAfterBegin";
        Assert.NotEqual(Format(sql, Choice(boundary, "never")), Format(sql, Choice(boundary, "never"), Indent(key, newlineOnly: false)));
    }
    [Theory]
    [InlineData("routine.parameters.spaceBeforeOpen", Function, "dbo.f (", "dbo.f(")]
    [InlineData("routine.parameters.spaceWithin", Function, "( @a int, @b int )", "(@a int, @b int)")]
    [InlineData("routine.parameters.spaceWithinEmpty", Empty, "( )", "()")]
    [InlineData("view.columns.spaceBeforeOpen", View, "dbo.v (", "dbo.v(")]
    [InlineData("view.columns.spaceWithin", View, "( id, a )", "(id, a)")]
    public void Every_brace_space_rule_has_distinct_insert_remove_modes(string key, string sql, string spaced, string tight)
    {
        Assert.Contains(spaced, Format(sql, Choice(key, "insert")));
        Assert.Contains(tight, Format(sql, Choice(key, "remove")));
    }
    [Theory]
    [InlineData("routine.parameters", Proc)]
    [InlineData("routine.parameters", Function)]
    [InlineData("routine.with", Proc)]
    [InlineData("routine.with", Function)]
    [InlineData("view.columns", View)]
    public void Every_list_has_on_off_auto_and_leading_comma_modes(string prefix, string sql)
    {
        var flat = Format(sql, Choice(prefix + ".stackList", "off"));
        var vertical = Format(sql, Choice(prefix + ".stackList", "on"));
        Assert.NotEqual(flat, vertical);
        Assert.Equal(flat, Format(sql, Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto")));
        Assert.Equal(Format(sql, Options(6), Choice(prefix + ".stackList", "on")),
            Format(sql, Options(6), Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto")));
        Assert.Contains("\n,", Format(sql, Choice(prefix + ".stackList", "on"), Choice("stackedList.commaPlacement", "leading")));
    }
    [Fact]
    public void View_query_compactness_thresholds_and_whole_query_indent_work()
    {
        var compact = Format(View, ("view.query.singleLine.any", RuleValue.FromBoolean(true)));
        Assert.Contains("SELECT id, a FROM dbo.T", compact);
        Assert.Equal(compact, Format(View, ("view.query.singleLine.whenFitsMargin", RuleValue.FromBoolean(true))));
        Assert.NotEqual(compact, Format(View, Options(6), ("view.query.singleLine.whenFitsMargin", RuleValue.FromBoolean(true))));
        foreach (var field in new[] { "maxWords", "maxCharacters" })
        {
            var key = "view.query.singleLine." + field;
            Assert.Equal(compact, Format(View, (key, RuleValue.FromThreshold(new ThresholdRule(true, 1000)))));
            Assert.Equal(Format(View), Format(View, (key, RuleValue.FromThreshold(new ThresholdRule(false, 1000)))));
            Assert.Equal(Format(View), Format(View, (key, RuleValue.FromThreshold(new ThresholdRule(true, 1)))));
        }
        Assert.Contains("\n    FROM dbo.T", Format(View, Choice("view.query.breakAfterAs", "always"), Indent("view.query.queryIndent")));
        Format(View.Replace("FROM", "-- keep\nFROM"), ("view.query.singleLine.any", RuleValue.FromBoolean(true)));
    }
    [Fact]
    public void Combined_modules_inline_table_functions_clr_and_v2_are_safe()
    {
        var keys = Ledger("SC-21").Select(r => r[3]).Distinct().ToArray();
        var rules = keys.Select(key => (key, RuleCatalog.Default.Definitions[key].DefaultValue.Kind switch
        {
            RuleValueKind.Indent => RuleValue.FromIndent(new IndentRule(true, 1)),
            RuleValueKind.Boolean => RuleValue.FromBoolean(false),
            RuleValueKind.Threshold => RuleValue.FromThreshold(new ThresholdRule(false, 100)),
            _ => RuleValue.FromChoice(RuleCatalog.Default.Definitions[key].Choices.Contains("always") ? "always"
                : RuleCatalog.Default.Definitions[key].Choices.Contains("on") ? "on"
                : RuleCatalog.Default.Definitions[key].Choices.Contains("insert") ? "insert" : "auto")
        })).ToArray();
        foreach (var sql in new[] { Proc, Function, Empty, Table, View,
            "CREATE FUNCTION dbo.f(@a int) RETURNS TABLE AS RETURN SELECT @a AS id;",
            "CREATE FUNCTION dbo.f(@a int) RETURNS TABLE RETURN SELECT @a AS id;",
            "CREATE FUNCTION dbo.f(@a int) RETURNS int AS EXTERNAL NAME a.b.c;" })
        {
            Format(sql, rules);
            Format(sql.Replace("CREATE", "ALTER"), rules);
            Format(sql.Replace("AS BEGIN", "AS /* preserve */ BEGIN"), rules);
        }
        var serializer = new SqlFormatterConfigurationSerializer();
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        Assert.Equal(values.Overrides, serializer.Deserialize(serializer.Serialize(Options().With(rules: values))).Rules.Overrides);
        Assert.Equal(Format(Proc), new ScriptDomSqlFormatter().Format(Proc,
            serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.False(serializer.Parse("""{"version":2,"rules":{"routine.parameters.stackMode":"unknown"}}""").Succeeded);
    }

    [Fact]
    public async Task Cli_documented_json_scoped_modules_and_invalid_sql_are_checked()
    {
        const string json = """
        {"version":2,"rules":{"routine.parameters.stackList":"on","routine.with.breakBefore":"always",
        "routine.body.breakBeforeAs":"always","routine.body.breakBefore":"always",
        "routine.body.codeIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
        "view.columns.stackList":"off","view.query.singleLine.any":true}}
        """;
        var serializer = new SqlFormatterConfigurationSerializer();
        var options = serializer.Deserialize(json);
        var formatter = new ScriptDomSqlFormatter();
        var script = Proc + "\nGO\n" + View;
        Assert.EndsWith("\nGO\n" + View, formatter.Format(script, options,
            new FormatRequest(FormatScope.Statement, new TSqlFormatter.Core.Parsing.SqlTextSpan(1, 0))).Text);
        foreach (var sql in new[] { "CREATE FUNCTION broken", "CREATE PROCEDURE dbo.p AS PRINT 'x\ny';" })
            Assert.Equal(sql, formatter.Format(sql, options, new FormatRequest()).Text);
        Format(Proc.Replace("CREATE", "CREATE OR ALTER"), Choice("routine.parameters.stackList", "on"));
        Format(Function.Replace("CREATE", "CREATE OR ALTER"), Choice("routine.parameters.stackList", "on"));
        Format(View.Replace("CREATE", "CREATE OR ALTER"), Choice("view.columns.stackList", "on"));
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, script);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await TSqlFormatter.Cli.SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(formatter.Format(script, options, new FormatRequest()).Text, output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
