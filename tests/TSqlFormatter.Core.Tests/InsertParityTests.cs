using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class InsertParityTests
{
    private const string Values = "INSERT INTO dbo.T (a, b) VALUES (1, 'x'), (2, 'y');";
    private const string Output = "INSERT INTO dbo.T (a, b) OUTPUT inserted.a, inserted.b VALUES (1, 'x');";
    private const string Select = "INSERT INTO dbo.T (a) SELECT a FROM dbo.S;";

    [Fact]
    public void Catalog_and_ledger_cover_all_insert_paths()
    {
        var keys = RuleCatalog.Default.Definitions.Keys.Where(key => key.StartsWith("insert.")).ToArray();
        Assert.Equal(38, keys.Length);
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "coverage.tsv"))
            .Skip(1).Select(line => line.Split('\t')).Where(row => row[1] == "SC-14").ToArray();
        Assert.Equal(60, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal("covered", row[2]);
            Assert.Contains(row[3], keys);
            Assert.NotEqual("-", row[4]);
            Assert.NotEqual("-", row[5]);
        });
        Assert.Equal(keys.OrderBy(key => key), rows.Select(row => row[3]).Distinct().OrderBy(key => key));
    }

    [Theory]
    [InlineData("insert.into.breakBefore", Values, "INSERT\nINTO")]
    [InlineData("insert.into.breakBeforeTable", Values, "INTO\ndbo.T")]
    [InlineData("insert.columns.breakBeforeOpen", Values, "dbo.T\n(")]
    [InlineData("insert.columns.breakAfterOpen", Values, "(\na")]
    [InlineData("insert.columns.breakBeforeClose", Values, "b\n)")]
    [InlineData("insert.output.breakBefore", Output, ")\nOUTPUT")]
    [InlineData("insert.output.breakAfter", Output, "OUTPUT\ninserted.a")]
    [InlineData("insert.source.breakBefore", Select, ")\nSELECT")]
    [InlineData("insert.values.breakBeforeKeyword", Values, ")\nVALUES")]
    [InlineData("insert.values.breakAfterKeyword", Values, "VALUES\n(")]
    [InlineData("insert.values.breakAfterOpen", Values, "(\n    1")]
    [InlineData("insert.values.breakBeforeClose", Values, "'x'\n)")]
    public void Boundaries_offer_distinct_always_and_never_policies(string key, string sql, string expected)
    {
        var expanded = Format(sql, Choice(key, "always"));
        var compact = Format(sql, Choice(key, "never"));
        Assert.NotEqual(expanded, compact);
        Assert.Contains(expected, expanded);
    }

    [Theory]
    [InlineData("insert.columns.spaceBeforeOpen", Values, "dbo.T (", "dbo.T(")]
    [InlineData("insert.columns.spaceWithin", Values, "( a, b )", "(a, b)")]
    [InlineData("insert.values.spaceWithin", Values, "( 1, 'x' )", "(1, 'x')")]
    public void Local_spaces_do_not_change_other_parentheses(string key, string sql, string spaced, string tight)
    {
        Assert.Contains(spaced, Format(sql, Choice(key, "insert")));
        Assert.Contains(tight, Format(sql, Choice(key, "remove")));
    }

    [Fact]
    public void Values_keyword_spacing_respects_the_line_break_policy()
    {
        Assert.Contains("VALUES(", Format(Values, Choice("insert.values.breakAfterKeyword", "never"),
            Choice("insert.values.spaceAfterKeyword", "remove")));
        Assert.Contains("VALUES (", Format(Values, Choice("insert.values.breakAfterKeyword", "never"),
            Choice("insert.values.spaceAfterKeyword", "insert")));
        Assert.Contains("VALUES\n(", Format(Values, Choice("insert.values.breakAfterKeyword", "always"),
            Choice("insert.values.spaceAfterKeyword", "remove")));
        Assert.Contains("(\n", Format(Values, Choice("insert.values.breakAfterOpen", "always"),
            Choice("insert.values.spaceWithin", "remove")));
    }

    [Theory]
    [InlineData("columns", Values)]
    [InlineData("values", Values)]
    [InlineData("output", Output)]
    public void Each_list_can_be_stacked_compact_or_automatic(string group, string sql)
    {
        var prefix = "insert." + group;
        var vertical = Format(sql, Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "onePerLine"));
        var compact = Format(sql, Choice(prefix + ".stackList", "off"));
        var automatic = Format(sql, Choice(prefix + ".stackList", "on"), Choice(prefix + ".stackMode", "auto"));
        Assert.NotEqual(vertical, compact);
        Assert.Equal(compact, automatic);
        Assert.Equal(Format(sql), Format(sql, Choice(prefix + ".stackMode", "auto")));
        var narrow = Format(sql, Options(width: 6), Choice(prefix + ".stackList", "on"),
            Choice(prefix + ".stackMode", "auto"));
        Assert.Equal(vertical, narrow);
    }

    [Fact]
    public void Row_stacking_is_independent_from_expression_stacking()
    {
        var flat = Format(Values, Choice("insert.values.stackRows", "off"), Choice("insert.values.stackList", "off"));
        Assert.Contains("(1, 'x'), (2, 'y')", flat);
        var rows = Format(Values, Choice("insert.values.stackRows", "on"), Choice("insert.values.stackList", "off"));
        Assert.Contains("),\n", rows);
        var expressions = Format(Values, Choice("insert.values.stackRows", "off"), Choice("insert.values.stackList", "on"));
        Assert.Contains(",\n", expressions);
        Assert.Contains("), (", expressions);
        Assert.Equal(flat, Format(Values, Choice("insert.values.stackRows", "on"),
            Choice("insert.values.stackRowsMode", "auto"), Choice("insert.values.stackList", "off")));
        Assert.Equal(rows, Format(Values, Options(width: 12), Choice("insert.values.stackRows", "on"),
            Choice("insert.values.stackRowsMode", "auto"), Choice("insert.values.stackList", "off")));
    }

    [Theory]
    [InlineData("insert.into.keywordIndent", "insert.into.breakBefore", Values)]
    [InlineData("insert.into.tableIndent", "insert.into.breakBeforeTable", Values)]
    [InlineData("insert.columns.listIndent", "insert.columns.breakAfterOpen", Values)]
    [InlineData("insert.columns.braceIndent", "insert.columns.breakBeforeOpen", Values)]
    [InlineData("insert.output.keywordIndent", "insert.output.breakBefore", Output)]
    [InlineData("insert.output.listIndent", "insert.output.breakAfter", Output)]
    [InlineData("insert.values.keywordIndent", "insert.values.breakBeforeKeyword", Values)]
    [InlineData("insert.values.listIndent", "insert.values.breakAfterOpen", Values)]
    [InlineData("insert.values.braceIndent", "insert.values.breakAfterKeyword", Values)]
    public void Indentation_changes_its_own_boundary(string key, string breakKey, string sql)
    {
        var normal = Format(sql, Choice(breakKey, "always"));
        var indented = Format(sql, Choice(breakKey, "always"), Indent(key, 2));
        Assert.NotEqual(normal, indented);
        Assert.Equal(normal, Format(sql, Choice(breakKey, "always"),
            (key, RuleValue.FromIndent(new IndentRule(false, 2)))));
    }

    [Fact]
    public void Inline_indent_switch_and_absolute_style_are_effective()
    {
        Assert.Equal(Format(Values), Format(Values, Indent("insert.into.tableIndent", 2)));
        Assert.Contains("INTO         dbo.T", Format(Values, Indent("insert.into.tableIndent", 2, false)));
        Assert.Equal(Format(Values), Format(Values, Indent("insert.into.tableIndent", 2, false, transparent: true)));
        var absolute = Format(Values, Choice("insert.columns.breakBeforeOpen", "always"),
            Indent("insert.columns.braceIndent", 2, style: "absolute"));
        Assert.Contains("dbo.T\n        (", absolute);
        Assert.Contains("dbo.T\n(", Format(Values, Choice("insert.columns.breakBeforeOpen", "always"),
            Indent("insert.columns.braceIndent", 2, transparent: true)));
        var rows = Format(Values, Indent("insert.values.braceIndent", 2));
        Assert.Contains("VALUES\n        (1, 'x'),\n        (2, 'y')", rows);
        var items = Format(Values, Indent("insert.columns.listIndent", 1, false));
        Assert.Contains("(     a,     b)", items);
    }

    [Fact]
    public void Source_indent_shifts_the_complete_select_body()
    {
        var indented = Format(Select, Indent("insert.source.indent", 2));
        Assert.Contains("\n        SELECT a\n        FROM dbo.S;", indented);
        var inline = Format(Select, Choice("insert.source.breakBefore", "never"), Indent("insert.source.indent", 2, false));
        Assert.Contains(")         SELECT", inline);
    }

    [Fact]
    public void Source_compactness_has_four_independent_conditions()
    {
        Assert.Contains("SELECT a FROM dbo.S", Format(Select, ("insert.source.singleLine.any", RuleValue.FromBoolean(true))));
        Assert.Contains("SELECT a FROM dbo.S", Format(Select, ("insert.source.singleLine.whenFitsMargin", RuleValue.FromBoolean(true))));
        Assert.Contains("SELECT a\n", Format(Select, Options(width: 12),
            ("insert.source.singleLine.whenFitsMargin", RuleValue.FromBoolean(true))));
        Assert.Contains("SELECT a FROM dbo.S", Format(Select, Options(width: 12),
            ("insert.source.singleLine.any", RuleValue.FromBoolean(true))));
        Assert.Contains("SELECT a\n", Format(Select, Threshold("insert.source.singleLine.maxWords", 5)));
        Assert.Contains("SELECT a FROM dbo.S", Format(Select, Threshold("insert.source.singleLine.maxWords", 6)));
        const string flat = "SELECT a FROM dbo.S";
        Assert.Contains("SELECT a\n", Format(Select, Threshold("insert.source.singleLine.maxCharacters", flat.Length)));
        Assert.Contains(flat, Format(Select, Threshold("insert.source.singleLine.maxCharacters", flat.Length + 1)));
        Assert.Contains("SELECT a\n", Format(Select,
            ("insert.source.singleLine.maxCharacters", RuleValue.FromThreshold(new ThresholdRule(false, 1000)))));
    }

    [Fact]
    public void Source_margin_includes_inline_insert_prefix()
    {
        var source = Format(Select, Options(width: 30), Choice("insert.source.breakBefore", "never"),
            ("insert.source.singleLine.whenFitsMargin", RuleValue.FromBoolean(true)));
        Assert.Contains("SELECT a\n", source);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.T EXEC dbo.GetRows;")]
    [InlineData("INSERT INTO dbo.T DEFAULT VALUES;")]
    public void Unsupported_sources_keep_their_layout(string sql)
    {
        Assert.Equal(sql, Format(sql, Choice("insert.into.breakBefore", "always"),
            Choice("insert.values.breakBeforeKeyword", "always")));
    }

    [Fact]
    public void Output_into_projection_is_separate_from_its_destination_columns()
    {
        const string sql = "INSERT INTO dbo.T (a, b) OUTPUT inserted.a, inserted.b INTO @audit (a, b) VALUES (1, 2);";
        var changed = Format(sql, Choice("insert.output.stackList", "on"), Choice("insert.columns.stackList", "on"));
        Assert.Contains("OUTPUT inserted.a,\ninserted.b INTO @audit (a, b)", changed);
    }

    [Fact]
    public void Comments_and_multiline_literals_are_preserved()
    {
        const string sql = "INSERT INTO dbo.T (a, /* column */ b) VALUES (1, 'literal'), /* row */ (2, 'other');";
        var changed = Format(sql, Choice("insert.columns.stackList", "on"), Choice("insert.values.stackRows", "off"));
        Assert.Contains("/* column */", changed);
        Assert.Contains("/* row */", changed);
        const string sourceComment = "INSERT INTO dbo.T SELECT a -- keep\nFROM dbo.S;";
        Assert.Contains("-- keep\n", Format(sourceComment, ("insert.source.singleLine.any", RuleValue.FromBoolean(true)),
            Indent("insert.source.indent", 2)));
        const string literal = "INSERT INTO dbo.T VALUES ('first\nsecond');";
        Assert.Equal(literal, Format(literal, Choice("insert.values.breakAfterOpen", "always")));
    }

    [Fact]
    public void Rules_respect_global_leading_comma_layout()
    {
        var changed = Format(Values, Choice("insert.columns.stackList", "on"),
            Choice("insert.values.stackList", "on"), Choice("insert.values.stackRows", "on"),
            Choice("stackedList.commaPlacement", "leading"), Choice("stackedList.spaceAfterLeadingComma", "remove"));
        Assert.Contains("a\n,b", changed);
        Assert.Contains("1\n    ,'x'", changed);
        Assert.Contains(")\n,(", changed);
    }

    [Theory]
    [InlineData(DocLineEnding.Lf, "\n")]
    [InlineData(DocLineEnding.CrLf, "\r\n")]
    [InlineData(DocLineEnding.Cr, "\r")]
    public void Source_indent_and_local_breaks_use_configured_line_endings(DocLineEnding ending, string newline)
    {
        var text = Format(Select, Options(ending: ending), Indent("insert.source.indent", 1));
        Assert.Contains(newline + "    SELECT a" + newline + "    FROM", text);
    }

    [Fact]
    public void Nested_code_variable_targets_and_neighboring_statements_are_isolated()
    {
        const string sql = "BEGIN INSERT INTO @target (a, b) VALUES (1, 2); END; SELECT a, b FROM dbo.S;";
        var changed = Format(sql, Choice("insert.columns.stackList", "on"));
        Assert.NotEqual(Format(sql), changed);
        Assert.Contains("SELECT a, b\nFROM dbo.S;", changed);
    }

    [Theory]
    [InlineData("WITH c AS (SELECT a FROM dbo.S) INSERT INTO dbo.T SELECT a FROM c;")]
    [InlineData("BEGIN INSERT INTO dbo.T SELECT a FROM dbo.S UNION ALL SELECT a FROM dbo.U; END;")]
    [InlineData("INSERT TOP (10) INTO dbo.T WITH (TABLOCK) (a, b) VALUES (1, 2);")]
    public void Complex_insert_forms_keep_tokens_and_stable_indentation(string sql)
    {
        Assert.NotEqual(Format(sql), Format(sql, Indent("insert.source.indent", 2),
            Choice("insert.into.breakBefore", "always"), Choice("insert.columns.stackList", "on")));
    }

    [Fact]
    public void Source_compaction_handles_sets_and_preserves_nested_literal_text()
    {
        const string sql = "INSERT dbo.T SELECT (SELECT 'inner text' FROM dbo.S) UNION ALL SELECT 'outer text';";
        var changed = Format(sql, ("insert.source.singleLine.any", RuleValue.FromBoolean(true)));
        Assert.Contains("UNION ALL SELECT 'outer text'", changed);
        Assert.Contains("'inner text'", changed);
        Assert.DoesNotContain('\n', changed.Substring(changed.IndexOf("SELECT", StringComparison.Ordinal)));
    }

    [Fact]
    public void V2_configuration_round_trips_and_v1_defaults_are_inherited()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var options = serializer.Deserialize("""{"version":2,"rules":{"insert.columns.stackList":"on","insert.values.spaceWithin":"insert","insert.source.indent":{"enabled":true,"offset":2,"onNewLineOnly":false,"style":"absolute","transparent":false},"insert.source.singleLine.maxWords":{"enabled":true,"value":6}}}""");
        var reloaded = serializer.Deserialize(serializer.Serialize(options));
        Assert.Equal(options.Rules.Overrides, reloaded.Rules.Overrides);
        Assert.Equal("inherit", serializer.Deserialize("""{"version":1}""").Rules.Get("insert.columns.stackList").Choice);
        Assert.Throws<ArgumentException>(() => new RuleOptions(RuleCatalog.Default).With("insert.values.stackMode", RuleValue.FromChoice("unknown")));
    }

    [Fact]
    public async Task Cli_loads_the_documented_insert_configuration()
    {
        const string json = """
        {
          "version": 2,
          "rules": {
            "insert.columns.breakAfterOpen": "always",
            "insert.columns.breakBeforeClose": "always",
            "insert.columns.stackList": "on",
            "insert.columns.listIndent": {"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
            "insert.values.breakAfterKeyword": "always",
            "insert.values.braceIndent": {"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
            "insert.values.stackList": "off",
            "insert.values.stackRows": "on"
          }
        }
        """;
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-insert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sqlPath = Path.Combine(directory, "query.sql");
            File.WriteAllText(sqlPath, Values);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await SqlFormatterCli.RunAsync(new[] { sqlPath }, input, output, error);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            const string expected = "INSERT INTO dbo.T (\n    a,\n    b\n)\nVALUES\n    (1, 'x'),\n    (2, 'y');";
            Assert.Equal(expected, output.ToString());
            Assert.Equal(expected, new ScriptDomSqlFormatter().Format(Values,
                new SqlFormatterConfigurationSerializer().Deserialize(json), new FormatRequest()).Text);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Statement_scope_applies_insert_rules_only_to_the_requested_statement()
    {
        var rules = new RuleOptions(RuleCatalog.Default).With("insert.columns.stackList", RuleValue.FromChoice("on"));
        var sql = Values + "\n" + Values;
        var result = new ScriptDomSqlFormatter().Format(sql, Options().With(rules: rules),
            new FormatRequest(FormatScope.Statement, new SqlTextSpan(1, 0)));
        Assert.True(result.ParseSucceeded);
        Assert.Contains("a,\nb", result.Text);
        Assert.EndsWith("\n" + Values, result.Text);
    }

    [Fact]
    public void Local_indent_handles_a_tab_indented_insert_inside_code()
    {
        var options = Options().With(indent: new IndentOptions(4, useTabs: true));
        var changed = Format("BEGIN INSERT INTO dbo.T SELECT a FROM dbo.S; END;", options,
            Indent("insert.source.indent", 1));
        Assert.Contains("\n        SELECT a", changed);
    }

    private static FormattingOptions Options(int width = 100, DocLineEnding ending = DocLineEnding.Lf) =>
        FormattingOptions.Default.With(general: new GeneralOptions(width, ending), keywords: new KeywordOptions(KeywordCase.Preserve));
    private static (string, RuleValue) Choice(string key, string value) => (key, RuleValue.FromChoice(value));
    private static (string, RuleValue) Threshold(string key, int value) => (key, RuleValue.FromThreshold(new ThresholdRule(true, value)));
    private static (string, RuleValue) Indent(string key, int offset, bool onNewLineOnly = true,
        string style = "relative", bool transparent = false) => (key, RuleValue.FromIndent(new IndentRule(true, offset, onNewLineOnly, style, transparent)));
    private static string Format(string sql, params (string Key, RuleValue Value)[] rules) => Format(sql, Options(), rules);
    private static string Format(string sql, FormattingOptions options, params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        options = options.With(rules: values);
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        var parser = new ScriptDomSqlParser();
        var before = parser.Parse(sql, SqlDialectVersion.Auto, CancellationToken.None);
        var after = parser.Parse(first.Text, SqlDialectVersion.Auto, CancellationToken.None);
        Assert.True(after.ParseSucceeded);
        Assert.Equal(Tokens(before), Tokens(after));
        return first.Text;
    }
    private static IEnumerable<string> Tokens(SqlParseResult parsed) => parsed.Tokens.Where(token =>
        token.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).Select(token => token.Text);
}
