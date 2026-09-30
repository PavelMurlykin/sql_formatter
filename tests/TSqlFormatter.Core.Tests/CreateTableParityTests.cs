using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using static TSqlFormatter.Core.Tests.ParityTestSupport;

namespace TSqlFormatter.Core.Tests;

public sealed class CreateTableParityTests
{
    private const string Sql = "CREATE TABLE dbo.T (id int, a decimal(10, 2) DEFAULT (1), "
        + "CONSTRAINT pk PRIMARY KEY (id), CONSTRAINT ck CHECK (a >= 0)) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY] "
        + "WITH (DATA_COMPRESSION = PAGE ON PARTITIONS (1), DATA_COMPRESSION = ROW ON PARTITIONS (2, 3));";
    [Fact]
    public void Ledger_covers_all_create_table_fields()
    { CheckLedger("SC-22", 20, nameof(CreateTableParityTests)); Assert.Equal(14, Ledger("SC-22").Select(r => r[3]).Distinct().Count()); }

    [Theory]
    [InlineData("columns.breakBeforeOpen", "(")]
    [InlineData("columns.breakAfterOpen", "id")]
    [InlineData("columns.breakBeforeClose", ")")]
    [InlineData("storage.breakBefore", "ON")]
    public void Every_boundary_supports_distinct_modes(string key, string token)
    {
        key = "createTable." + key;
        var expanded = Format(Sql, Choice(key, "always"));
        Assert.Contains("\n" + token, expanded);
        Assert.NotEqual(expanded, Format(Sql, Choice(key, "never")));
        Assert.Equal(Format(Sql), Format(Sql, Choice(key, "inherit")));
    }
    [Theory]
    [InlineData("columns.braceIndent", "columns.breakBeforeOpen", "(")]
    [InlineData("columns.listIndent", "columns.breakAfterOpen", "id")]
    [InlineData("storage.listIndent", "storage.breakBefore", "ON")]
    public void Every_indent_honors_native_fields(string key, string boundary, string token)
    {
        key = "createTable." + key;
        boundary = "createTable." + boundary;
        Assert.Contains("\n    " + token, Format(Sql, Choice(boundary, "always"), Indent(key)));
        Assert.Contains("\n        " + token, Format(Sql, Choice(boundary, "always"), Indent(key, 2)));
        Assert.Equal(Format(Sql, Choice(boundary, "always")), Format(Sql, Choice(boundary, "always"),
            (key, RuleValue.FromIndent(new IndentRule(false, 2)))));
        Assert.Contains("\n" + token, Format(Sql, Choice(boundary, "always"), Indent(key, transparent: true)));
        Format(Sql, Choice(boundary, "always"), Indent(key, -1, style: "absolute"));
        Assert.NotEqual(Format(Sql, Choice(boundary, "never")), Format(Sql, Choice(boundary, "never"), Indent(key, newlineOnly: false)));
    }
    [Theory]
    [InlineData("columns.spaceBeforeOpen", "dbo.T (", "dbo.T(")]
    [InlineData("columns.spaceWithin", "( id", "(id")]
    public void Spaces_are_scoped_to_definition_braces(string key, string spaced, string tight)
    {
        key = "createTable." + key;
        Assert.Contains(spaced, Format(Sql, Choice(key, "insert")));
        Assert.Contains(tight, Format(Sql, Choice(key, "remove")));
        Assert.Contains("decimal(10, 2)", Format(Sql, Choice(key, "insert")));
    }
    [Theory]
    [InlineData("columns")]
    [InlineData("storage")]
    public void Lists_stack_compact_auto_and_preserve_inner_commas(string group)
    {
        var prefix = "createTable." + group;
        var flat = Format(Sql, Choice(prefix + ".stackList", "off"));
        var vertical = Format(Sql, Choice(prefix + ".stackList", "on"));
        Assert.NotEqual(flat, vertical);
        Assert.Equal(flat, Format(Sql, Options(1000), Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto")));
        Assert.Equal(vertical, Format(Sql, Options(6), Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto")));
        Assert.Contains("\n,", Format(Sql, Choice(prefix + ".stackList", "on"), Choice("stackedList.commaPlacement", "leading")));
        Assert.Contains("decimal(10, 2)", vertical);
    }
    [Fact]
    public void Blank_lines_and_storage_clauses_have_real_effects()
    {
        var script = "PRINT 1;\n" + Sql + "\nPRINT 2;";
        Assert.Contains(";\n\nCREATE", Format(script, ("createTable.blankLinesAround", RuleValue.FromBoolean(true))));
        Assert.Contains(";\n\nPRINT 2", Format(script, ("createTable.blankLinesAround", RuleValue.FromBoolean(true))));
        Assert.Equal(Format(script), Format(script, ("createTable.blankLinesAround", RuleValue.FromBoolean(false))));
        var storage = Format(Sql, Choice("createTable.storage.breakBefore", "always"), Indent("createTable.storage.listIndent"));
        Assert.Contains("\n    ON [PRIMARY]", storage);
        Assert.Contains("\n    TEXTIMAGE_ON [PRIMARY]", storage);
        Assert.Contains("\n    WITH (", storage);
        Assert.Contains("\n    FILESTREAM_ON [FS]", Format(
            "CREATE TABLE dbo.F (id uniqueidentifier ROWGUIDCOL UNIQUE, data varbinary(max) FILESTREAM) ON [PRIMARY] FILESTREAM_ON [FS];",
            Choice("createTable.storage.breakBefore", "always"), Indent("createTable.storage.listIndent")));
    }
    [Fact]
    public void Temporal_computed_constraint_index_and_graph_tables_preserve_tokens_and_order()
    {
        var rules = new[] { Choice("createTable.columns.breakBeforeOpen", "always"), Indent("createTable.columns.braceIndent"),
            Choice("createTable.columns.breakAfterOpen", "always"), Indent("createTable.columns.listIndent"),
            Choice("createTable.columns.breakBeforeClose", "always"), Choice("createTable.columns.spaceWithin", "remove"),
            Choice("createTable.columns.stackList", "on"), Choice("createTable.columns.stackMode", "auto"),
            Choice("createTable.storage.breakBefore", "always"), Indent("createTable.storage.listIndent"),
            Choice("createTable.storage.stackList", "on") };
        foreach (var sql in new[] { Sql, Sql.Replace("a decimal(10, 2) DEFAULT (1)", "a AS (id + 1) PERSISTED"),
            "CREATE TABLE dbo.T (id int, a int, INDEX ix NONCLUSTERED (a, id));",
            "CREATE TABLE dbo.T (id int PRIMARY KEY, a int) AS NODE;",
            "CREATE TABLE dbo.T (id int PRIMARY KEY, s datetime2 GENERATED ALWAYS AS ROW START NOT NULL, "
            + "e datetime2 GENERATED ALWAYS AS ROW END NOT NULL, PERIOD FOR SYSTEM_TIME(s, e)) "
            + "WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.H), DATA_COMPRESSION = PAGE);",
            Sql.Replace(", CONSTRAINT ck", ", -- preserve\nCONSTRAINT ck") }) Format(sql, rules);
        Format("BEGIN " + Sql + " END;", rules);
        Format("BEGIN " + Sql.Replace(", CONSTRAINT ck", ", -- preserve\nCONSTRAINT ck") + " END;", rules);
    }
    [Fact]
    public async Task V2_cli_and_safe_fallback_match_the_documented_config()
    {
        const string json = """
        {"version":2,"rules":{"createTable.columns.breakBeforeOpen":"always","createTable.columns.breakAfterOpen":"always",
        "createTable.columns.breakBeforeClose":"always","createTable.columns.stackList":"on",
        "createTable.columns.listIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
        "createTable.storage.breakBefore":"always","createTable.storage.stackList":"on"}}
        """;
        var serializer = new SqlFormatterConfigurationSerializer();
        var options = serializer.Deserialize(json);
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var key in Ledger("SC-22").Select(r => r[3]).Distinct()) rules = rules.With(key, RuleCatalog.Default.Definitions[key].DefaultValue);
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(options.With(rules: rules))).Rules.Overrides);
        var formatter = new ScriptDomSqlFormatter();
        Assert.Equal(Format(Sql), formatter.Format(Sql, serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.Equal("CREATE TABLE dbo.T (???);", formatter.Format("CREATE TABLE dbo.T (???);", options, new FormatRequest()).Text);
        Assert.False(serializer.Parse("""{"version":2,"rules":{"createTable.storage.stackMode":"unknown"}}""").Succeeded);
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-table-" + Guid.NewGuid().ToString("N"));
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
