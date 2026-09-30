using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using static TSqlFormatter.Core.Tests.ParityTestSupport;

namespace TSqlFormatter.Core.Tests;

public sealed class DeclareParityTests
{
    private const string Variables = "DECLARE @a int, @longer decimal(10, 2) = 1, @s nvarchar(20) = N'x  y';";
    private const string Table = "DECLARE @t AS TABLE (id int PRIMARY KEY, a nvarchar(10));";
    private const string Cursor = "DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT id, a\nFROM dbo.T\nWHERE id > 0;";

    [Fact]
    public void Ledger_and_catalog_cover_all_declaration_paths()
    {
        CheckLedger("SC-19", 28, nameof(DeclareParityTests));
        Assert.Equal(16, Ledger("SC-19").Select(r => r[3]).Distinct().Count());
    }
    public static IEnumerable<object[]> Boundaries() => new[]
    {
        new object[] { "variables.breakAfter", Variables, "@a" },
        new object[] { "variables.breakBeforeTable", Table, "TABLE" },
        new object[] { "cursor.breakBefore", Cursor, "CURSOR" },
        new object[] { "cursor.breakBeforeFor", Cursor, "FOR" },
        new object[] { "cursor.breakBeforeQuery", Cursor, "SELECT" }
    };
    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Every_boundary_supports_all_modes(string key, string sql, string token)
    {
        var vertical = Format(sql, Choice("declare." + key, "always"));
        Assert.Contains("\n" + token, vertical);
        Assert.NotEqual(vertical, Format(sql, Choice("declare." + key, "never")));
        Assert.Equal(Format(sql), Format(sql, Choice("declare." + key, "inherit")));
    }
    public static IEnumerable<object[]> Indents() => new[]
    {
        new object[] { "variables.listIndent", "variables.breakAfter", Variables, "@a" },
        new object[] { "variables.tableIndent", "variables.breakBeforeTable", Table, "TABLE" },
        new object[] { "cursor.keywordIndent", "cursor.breakBefore", Cursor, "CURSOR" },
        new object[] { "cursor.forIndent", "cursor.breakBeforeFor", Cursor, "FOR" },
        new object[] { "cursor.queryIndent", "cursor.breakBeforeQuery", Cursor, "SELECT" }
    };
    [Theory]
    [MemberData(nameof(Indents))]
    public void Every_indent_supports_enabled_offset_newline_style_and_transparency(string key, string boundary, string sql, string token)
    {
        key = "declare." + key;
        boundary = "declare." + boundary;
        var one = Format(sql, Choice(boundary, "always"), Indent(key));
        Assert.Contains("\n    " + token, one);
        Assert.NotEqual(one, Format(sql, Choice(boundary, "always"), Indent(key, 2)));
        Assert.Equal(Format(sql, Choice(boundary, "always")), Format(sql, Choice(boundary, "always"),
            (key, RuleValue.FromIndent(new IndentRule(false, 2)))));
        Assert.Contains("\n" + token, Format(sql, Choice(boundary, "always"), Indent(key, transparent: true)));
        Format(sql, Choice(boundary, "always"), Indent(key, -1, style: "absolute"));
        Assert.Equal(Format(sql, Choice(boundary, "never")), Format(sql, Choice(boundary, "never"), Indent(key)));
        Assert.Contains("     " + token, Format(sql, Choice(boundary, "never"), Indent(key, newlineOnly: false)));
    }

    [Fact]
    public void Variable_lists_preserve_alignment_when_vertical_and_compact_when_requested()
    {
        var flat = Format(Variables, Choice("declare.variables.stackList", "off"));
        var vertical = Format(Variables, Choice("declare.variables.stackList", "on"));
        Assert.NotEqual(flat, vertical);
        Assert.Equal(flat, Format(Variables, Choice("declare.variables.stackList", "on"), Choice("declare.variables.stackMode", "auto")));
        Assert.Equal(vertical, Format(Variables, Options(6), Choice("declare.variables.stackList", "on"), Choice("declare.variables.stackMode", "auto")));
        Assert.Contains("\n,", Format(Variables, Choice("declare.variables.stackList", "on"), Choice("stackedList.commaPlacement", "leading")));
        var aligned = Options().With(alignment: new AlignmentOptions(declareTypes: true));
        Assert.Contains("@a      int", Format("DECLARE @a int, @longer int;", aligned,
            Choice("declare.variables.stackList", "on")));
        Assert.Contains("@a int", Format("DECLARE @a int, @longer int;", aligned,
            Choice("declare.variables.breakAfter", "never"), Choice("declare.variables.stackList", "off")));
    }

    [Fact]
    public void Query_indent_shifts_all_lines_and_compaction_has_independent_thresholds()
    {
        var shifted = Format(Cursor, Choice("declare.cursor.breakBeforeQuery", "always"), Indent("declare.cursor.queryIndent"));
        Assert.Contains("\n    SELECT id, a\n    FROM dbo.T\n    WHERE", shifted);
        var compact = Format(Cursor, ("declare.cursor.singleLine.any", RuleValue.FromBoolean(true)));
        Assert.Contains("SELECT id, a FROM dbo.T WHERE id > 0", compact);
        Assert.NotEqual(compact, Format(Cursor));
        Assert.Equal(compact, Format(Cursor, ("declare.cursor.singleLine.whenFitsMargin", RuleValue.FromBoolean(true))));
        Assert.NotEqual(compact, Format(Cursor, Options(10), ("declare.cursor.singleLine.whenFitsMargin", RuleValue.FromBoolean(true))));
        foreach (var field in new[] { "maxWords", "maxCharacters" })
        {
            var key = "declare.cursor.singleLine." + field;
            Assert.Equal(compact, Format(Cursor, (key, RuleValue.FromThreshold(new ThresholdRule(true, 1000)))));
            Assert.Equal(Format(Cursor), Format(Cursor, (key, RuleValue.FromThreshold(new ThresholdRule(false, 1000)))));
            Assert.Equal(Format(Cursor), Format(Cursor, (key, RuleValue.FromThreshold(new ThresholdRule(true, 1)))));
        }
        Assert.Contains("-- preserve\n", Format(Cursor.Replace("\nFROM", " -- preserve\nFROM"),
            ("declare.cursor.singleLine.any", RuleValue.FromBoolean(true))));
    }

    [Fact]
    public void Nested_declarations_combinations_and_v2_round_trips_are_stable()
    {
        var sql = "BEGIN " + Variables + " " + Table + " " + Cursor + " END";
        Format(sql, Choice("declare.variables.breakAfter", "always"), Indent("declare.variables.listIndent"),
            Choice("declare.variables.stackList", "on"), Choice("declare.variables.breakBeforeTable", "always"),
            Indent("declare.variables.tableIndent"), Choice("declare.cursor.breakBefore", "always"),
            Choice("declare.cursor.breakBeforeFor", "always"), Choice("declare.cursor.breakBeforeQuery", "always"),
            Indent("declare.cursor.queryIndent"));
        var serializer = new SqlFormatterConfigurationSerializer();
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var key in Ledger("SC-19").Select(r => r[3]).Distinct())
            rules = rules.With(key, RuleCatalog.Default.Definitions[key].DefaultValue);
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(Options().With(rules: rules))).Rules.Overrides);
        Assert.Equal(Format(Variables), new ScriptDomSqlFormatter().Format(Variables,
            serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.False(serializer.Parse("""{"version":2,"rules":{"declare.variables.stackMode":"unknown"}}""").Succeeded);
    }

    [Fact]
    public async Task Documented_json_works_through_cli_and_unsafe_inputs_are_unchanged()
    {
        const string json = """
        {"version":2,"rules":{"declare.variables.breakAfter":"always","declare.variables.stackList":"on",
        "declare.variables.listIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
        "declare.cursor.breakBeforeFor":"always","declare.cursor.breakBeforeQuery":"always"}}
        """;
        var options = new SqlFormatterConfigurationSerializer().Deserialize(json);
        var formatter = new ScriptDomSqlFormatter();
        foreach (var sql in new[] { "DECLARE @a ???", "DECLARE @s nvarchar(20) = N'x\ny';" })
            Assert.Equal(sql, formatter.Format(sql, options, new FormatRequest()).Text);
        Format(Variables.Replace(", @longer", ", -- preserve\n@longer"), Choice("declare.variables.stackList", "off"));
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-declare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, Variables + "\n" + Cursor);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await TSqlFormatter.Cli.SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(formatter.Format(Variables + "\n" + Cursor, options, new FormatRequest()).Text, output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
