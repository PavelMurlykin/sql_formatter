using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using static TSqlFormatter.Core.Tests.ParityTestSupport;

namespace TSqlFormatter.Core.Tests;

public sealed class ExecuteLabelParityTests
{
    private const string Exec = "EXEC @rc = dbo.p @a = 1, @b = N'x, y', @c = @value OUTPUT;";
    private const string Label = "PRINT 0;\nstart: BEGIN EXEC dbo.p 1, 2; END;\nPRINT 2; GOTO start;";
    [Fact]
    public void Ledger_has_no_remaining_unresolved_semantics_after_sc24()
    {
        CheckLedger("SC-24", 12, nameof(ExecuteLabelParityTests));
        Assert.Equal(7, Ledger("SC-24").Select(r => r[3]).Distinct().Count());
        var all = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "coverage.tsv"))
            .Skip(1).Select(line => line.Split('\t')).ToArray();
        Assert.Equal(977, all.Length);
        Assert.Equal(969, all.Count(row => row[2] == "covered"));
        Assert.Equal(8, all.Count(row => row[2] == "not_applicable"));
    }
    [Theory]
    [InlineData("execute.parameters.breakBefore", Exec)]
    [InlineData("labels.breakAfter", Label)]
    public void Boundaries_have_distinct_modes(string key, string sql)
    {
        Assert.NotEqual(Format(sql, Choice(key, "always")), Format(sql, Choice(key, "never")));
        Assert.Equal(Format(sql), Format(sql, Choice(key, "inherit")));
    }
    [Theory]
    [InlineData("execute.parameters.listIndent", "execute.parameters.breakBefore", Exec, "@a")]
    [InlineData("labels.indent", "labels.breakAfter", Label, "start:")]
    public void Indents_honor_enabled_offset_newline_style_and_transparency(string key, string boundary, string sql, string token)
    {
        Assert.Contains("\n    " + token, Format(sql, Choice(boundary, "always"), Indent(key)));
        Assert.Contains("\n        " + token, Format(sql, Choice(boundary, "always"), Indent(key, 2)));
        Assert.Equal(Format(sql, Choice(boundary, "always")), Format(sql, Choice(boundary, "always"),
            (key, RuleValue.FromIndent(new IndentRule(false, 2)))));
        Assert.Contains("\n" + token, Format(sql, Choice(boundary, "always"), Indent(key, transparent: true)));
        Format(sql, Choice(boundary, "always"), Indent(key, -1, style: "absolute"));
        var inline = key.StartsWith("labels") ? sql.Replace(";\nstart:", "; start:") : sql;
        Assert.NotEqual(Format(inline, Choice(boundary, "never")), Format(inline, Choice(boundary, "never"), Indent(key, newlineOnly: false)));
    }
    [Fact]
    public void Execute_lists_support_all_modes_and_do_not_rewrite_dynamic_sql_literals()
    {
        var flat = Format(Exec, Choice("execute.parameters.stackList", "off"));
        var vertical = Format(Exec, Choice("execute.parameters.stackList", "on"));
        Assert.NotEqual(flat, vertical);
        Assert.Equal(flat, Format(Exec, Choice("execute.parameters.stackList", "on"), Choice("execute.parameters.stackMode", "auto")));
        Assert.Equal(vertical, Format(Exec, Options(6), Choice("execute.parameters.stackList", "on"), Choice("execute.parameters.stackMode", "auto")));
        Assert.Contains("\n,", Format(Exec, Choice("execute.parameters.stackList", "on"), Choice("stackedList.commaPlacement", "leading")));
        foreach (var sql in new[] { "EXEC dbo.p 1, 2, DEFAULT;", "EXECUTE @name @a = 1, @b = 2;",
            "EXEC sys.sp_executesql N'SELECT @a, @b', N'@a int, @b int', @a = 1, @b = 2;",
            "EXEC (N'SELECT ?, ?', @a, @b) AT [remote];", "EXEC (N'SELECT a, b FROM T');",
            Exec.Replace(", @b", ", -- keep\n@b") })
            Format(sql, Choice("execute.parameters.breakBefore", "always"), Indent("execute.parameters.listIndent"),
                Choice("execute.parameters.stackList", "on"));
    }
    [Fact]
    public void Label_start_absolute_relative_blank_lines_and_following_block_are_stable()
    {
        Assert.StartsWith("    start:", Format("start: PRINT 1;", Indent("labels.indent"), Choice("labels.breakAfter", "always")));
        var nested = "BEGIN\nstart: PRINT 1; GOTO start; END;";
        Assert.Contains("\n        start:", Format(nested, Indent("labels.indent", 2)));
        Format(nested, Indent("labels.indent", -1, style: "absolute"));
        var inner = "BEGIN BEGIN\nstart: PRINT 1; GOTO start; END; END;";
        Assert.Contains("\n        start:", Format(inner, Indent("labels.indent")));
        Assert.Contains("\n    start:", Format(inner, Indent("labels.indent", style: "absolute")));
        var blank = Format(Label, ("labels.blankLinesAround", RuleValue.FromBoolean(true)));
        Assert.Contains(";\n\nstart:", blank);
        Assert.Contains("END;\n\nPRINT 2", blank);
        Assert.Equal(Format(Label), Format(Label, ("labels.blankLinesAround", RuleValue.FromBoolean(false))));
        Assert.Contains("start:\n", Format(Label, Choice("labels.breakAfter", "always")));
        Format("BEGIN " + Label + " END;", Indent("labels.indent"), Choice("labels.breakAfter", "always"),
            ("labels.blankLinesAround", RuleValue.FromBoolean(true)), Choice("execute.parameters.stackList", "on"));
        Format("start: -- keep\nPRINT 1; GOTO start;", Choice("labels.breakAfter", "never"));
        Format("start: second: PRINT 1; GOTO second;", Choice("labels.breakAfter", "always"));
    }
    [Fact]
    public async Task V2_cli_defaults_and_invalid_inputs_are_checked()
    {
        const string json = """
        {"version":2,"rules":{"execute.parameters.breakBefore":"always","execute.parameters.stackList":"on",
        "execute.parameters.listIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
        "labels.breakAfter":"always","labels.blankLinesAround":true,
        "labels.indent":{"enabled":true,"offset":0,"onNewLineOnly":true,"style":"absolute","transparent":false}}}
        """;
        var serializer = new SqlFormatterConfigurationSerializer();
        var options = serializer.Deserialize(json);
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var key in Ledger("SC-24").Select(r => r[3]).Distinct()) rules = rules.With(key, RuleCatalog.Default.Definitions[key].DefaultValue);
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(options.With(rules: rules))).Rules.Overrides);
        var formatter = new ScriptDomSqlFormatter();
        Assert.Equal(Format(Exec), formatter.Format(Exec, serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.Equal("EXEC ???", formatter.Format("EXEC ???", options, new FormatRequest()).Text);
        Assert.False(serializer.Parse("""{"version":2,"rules":{"labels.indent":{"enabled":true,"offset":0,"onNewLineOnly":true,"style":"numeric2","transparent":false}}}""").Succeeded);
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-exec-label-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, Label + "\n" + Exec);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await TSqlFormatter.Cli.SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(formatter.Format(Label + "\n" + Exec, options, new FormatRequest()).Text, output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
