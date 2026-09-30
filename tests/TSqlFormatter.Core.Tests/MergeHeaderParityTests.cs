using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class MergeHeaderParityTests
{
    private const string Basic = "MERGE INTO dbo.T AS t USING dbo.S AS s ON t.id = s.id AND s.a = 1 OR s.a = 2 WHEN MATCHED THEN DELETE;";
    private const string Hints = "MERGE INTO dbo.T WITH (HOLDLOCK, UPDLOCK) AS t USING dbo.S AS s ON t.id = s.id WHEN MATCHED THEN DELETE;";
    private const string Join = "MERGE INTO dbo.T AS t USING dbo.S AS s INNER JOIN dbo.U AS u ON s.id = u.id AND u.a = 1 OR u.a = 2 ON t.id = s.id WHEN MATCHED THEN DELETE OPTION (RECOMPILE);";
    private const string Values = "MERGE INTO dbo.T AS t USING (VALUES (1, 'x'), (2, 'y')) AS s(id, a) ON t.id = s.id WHEN MATCHED THEN DELETE;";

    [Fact]
    public void Catalog_and_ledger_cover_all_merge_header_paths()
    {
        var keys = RuleCatalog.Default.Definitions.Keys.Where(IsHeaderRule).ToArray();
        Assert.Equal(50, keys.Length);
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "coverage.tsv"))
            .Skip(1).Select(line => line.Split('\t')).Where(row => row[1] == "SC-16").ToArray();
        Assert.Equal(86, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal("covered", row[2]);
            Assert.Contains(row[3], keys);
            Assert.StartsWith(nameof(MergeHeaderParityTests) + ".", row[4]);
            Assert.NotEqual("-", row[5]);
        });
        Assert.Equal(keys.OrderBy(key => key), rows.Select(row => row[3]).Distinct().OrderBy(key => key));
    }

    public static IEnumerable<object[]> Boundaries() => new[]
    {
        new object[] { "into.breakBefore", Basic, "INTO" },
        new object[] { "into.breakBeforeTable", Basic, "dbo.T" },
        new object[] { "hints.breakBefore", Hints, "WITH" },
        new object[] { "hints.breakBeforeOpen", Hints, "(" },
        new object[] { "hints.breakAfterOpen", Hints, "HOLDLOCK" },
        new object[] { "hints.breakBeforeClose", Hints, ")" },
        new object[] { "using.breakBefore", Basic, "USING" },
        new object[] { "using.breakAfter", Basic, "dbo.S" },
        new object[] { "on.breakBefore", Basic, "ON" },
        new object[] { "on.breakAfter", Basic, "t.id" },
        new object[] { "join.breakBefore", Join, "INNER JOIN" },
        new object[] { "join.breakAfter", Join, "dbo.U" },
        new object[] { "join.onBreakBefore", Join, "ON" },
        new object[] { "join.onBreakAfter", Join, "s.id" },
        new object[] { "values.breakBeforeKeyword", Values, "VALUES" },
        new object[] { "values.breakAfterKeyword", Values, "(" },
        new object[] { "values.breakAfterOpen", Values, "1" },
        new object[] { "values.breakBeforeClose", Values, ")" }
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Every_boundary_supports_always_never_and_inherit(string key, string sql, string next)
    {
        var expanded = Format(sql, Choice(key, "always"));
        Assert.Contains("\n" + next, expanded);
        Assert.NotEqual(expanded, Format(sql, Choice(key, "never")));
        Assert.Equal(Format(sql), Format(sql, Choice(key, "inherit")));
    }

    public static IEnumerable<object[]> Indents() => new[]
    {
        new object[] { "into.keywordIndent", "into.breakBefore", Basic, "INTO" },
        new object[] { "into.tableIndent", "into.breakBeforeTable", Basic, "dbo.T" },
        new object[] { "hints.keywordIndent", "hints.breakBefore", Hints, "WITH" },
        new object[] { "hints.braceIndent", "hints.breakBeforeOpen", Hints, "(" },
        new object[] { "hints.listIndent", "hints.breakAfterOpen", Hints, "HOLDLOCK" },
        new object[] { "using.keywordIndent", "using.breakBefore", Basic, "USING" },
        new object[] { "on.keywordIndent", "on.breakBefore", Basic, "ON" },
        new object[] { "on.conditionIndent", "on.breakAfter", Basic, "t.id" },
        new object[] { "on.nestedConditionIndent", "on.wrapAfterOperator", Basic, "s.a" },
        new object[] { "join.keywordIndent", "join.breakBefore", Join, "INNER JOIN" },
        new object[] { "join.tableIndent", "join.breakAfter", Join, "dbo.U" },
        new object[] { "join.onKeywordIndent", "join.onBreakBefore", Join, "ON" },
        new object[] { "join.onConditionIndent", "join.onBreakAfter", Join, "s.id" },
        new object[] { "join.nestedConditionIndent", "join.wrapAfterOperator", Join, "u.a" },
        new object[] { "values.keywordIndent", "values.breakBeforeKeyword", Values, "VALUES" },
        new object[] { "values.braceIndent", "values.breakAfterKeyword", Values, "(" },
        new object[] { "values.listIndent", "values.breakAfterOpen", Values, "1" }
    };

    [Theory]
    [MemberData(nameof(Indents))]
    public void Every_indent_targets_a_real_boundary_and_honors_enabled_offset_and_newline(string key, string boundary, string sql, string next)
    {
        var one = Format(sql, Choice(boundary, "always"), Indent(key, 1));
        var two = Format(sql, Choice(boundary, "always"), Indent(key, 2));
        Assert.Contains("\n    " + next, one);
        Assert.Contains("\n        " + next, two);
        Assert.NotEqual(one, two);
        Assert.Equal(Format(sql, Choice(boundary, "always")), Format(sql, Choice(boundary, "always"),
            ("merge." + key, RuleValue.FromIndent(new IndentRule(false, 2, true)))));
        Assert.Equal(Format(sql, Choice(boundary, "never")), Format(sql, Choice(boundary, "never"), Indent(key, 1)));
        Assert.Contains("     " + next, Format(sql, Choice(boundary, "never"), Indent(key, 1, false)));
    }

    [Theory]
    [InlineData("hints.spaceBeforeOpen", Hints, "WITH (", "WITH(")]
    [InlineData("hints.spaceWithin", Hints, "( HOLDLOCK, UPDLOCK )", "(HOLDLOCK, UPDLOCK)")]
    [InlineData("values.spaceAfterKeyword", Values, "VALUES (", "VALUES(")]
    [InlineData("values.spaceWithin", Values, "( 1, 'x' )", "(1, 'x')")]
    public void Local_spaces_change_only_the_requested_source_or_hint_gap(string key, string sql, string spaced, string tight)
    {
        Assert.Contains(spaced, Format(sql, Choice(key, "insert")));
        Assert.Contains(tight, Format(sql, Choice(key, "remove")));
        Assert.Equal(Format(sql), Format(sql, Choice(key, "inherit")));
    }

    [Theory]
    [InlineData("hints.breakBeforeOpen", "hints.spaceBeforeOpen", Hints, "WITH\n(")]
    [InlineData("hints.breakAfterOpen", "hints.spaceWithin", Hints, "(\nHOLDLOCK")]
    [InlineData("values.breakAfterKeyword", "values.spaceAfterKeyword", Values, "VALUES\n(")]
    [InlineData("values.breakAfterOpen", "values.spaceWithin", Values, "(\n1")]
    public void Explicit_newlines_take_precedence_over_local_space_removal(string boundary, string spacing, string sql, string expected)
    {
        Assert.Contains(expected, Format(sql, Choice(boundary, "always"), Choice(spacing, "remove")));
    }

    [Theory]
    [InlineData("stackList", "stackMode")]
    [InlineData("stackRows", "stackRowsMode")]
    public void Values_lists_and_rows_support_stacking_compaction_auto_and_leading_commas(string stack, string mode)
    {
        var vertical = Format(Values, Choice("values." + stack, "on"));
        var flat = Format(Values, Choice("values." + stack, "off"));
        Assert.NotEqual(vertical, flat);
        Assert.Equal(flat, Format(Values, Choice("values." + stack, "on"), Choice("values." + mode, "auto")));
        Assert.Equal(vertical, Format(Values, Options(width: 6), Choice("values." + stack, "on"), Choice("values." + mode, "auto")));
        Assert.Equal(Format(Values), Format(Values, Choice("values." + mode, "auto")));
        var leading = Format(Values, Choice("values." + stack, "on"), FullChoice("stackedList.commaPlacement", "leading"),
            FullChoice("stackedList.spaceAfterLeadingComma", "remove"));
        Assert.Contains("\n,", leading);
        Assert.DoesNotContain("\n, ", leading);
    }

    [Fact]
    public void Expression_stacking_and_row_stacking_are_independent_and_indents_apply_without_stacking()
    {
        Assert.Contains("VALUES (1, 'x'),\n(2, 'y')", Format(Values, Choice("values.stackRows", "on"), Choice("values.stackList", "off")));
        Assert.Contains("1,\n'x'", Format(Values, Choice("values.stackRows", "off"), Choice("values.stackList", "on")));
        var changed = Format(Values.Replace(", 'x'", ",\n'x'").Replace(", (2", ",\n(2"),
            Indent("values.listIndent", 1), Indent("values.braceIndent", 1));
        Assert.Contains(",\n    'x'", changed);
        Assert.Contains(",\n    (2", changed);
        var leading = Values.Replace(", 'x'", "\n, 'x'");
        Assert.Contains("\n    , 'x'", Format(leading, Indent("values.listIndent", 1)));
    }

    [Theory]
    [InlineData("on", "on")]
    [InlineData("on", "off")]
    [InlineData("off", "on")]
    [InlineData("off", "off")]
    public void Combined_expression_row_stacking_and_indentation_are_idempotent(string rows, string expressions)
    {
        Format(Values, Choice("values.stackRows", rows), Choice("values.stackList", expressions),
            Choice("values.breakAfterOpen", "always"), Indent("values.listIndent", 1));
        Format(Values.Replace(", (2", ",\n(2"), Choice("values.stackRows", rows), Choice("values.stackList", expressions),
            Choice("values.breakAfterOpen", "always"), Indent("values.listIndent", 1));
        Format(Values, Choice("values.stackRows", rows), Choice("values.stackList", expressions),
            Choice("values.breakAfterKeyword", "always"), Choice("values.breakAfterOpen", "always"),
            Choice("values.breakBeforeClose", "always"), Indent("values.braceIndent", 1), Indent("values.listIndent", 2));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(16)]
    public void Automatic_lists_have_stable_margin_decisions_with_explicit_brace_breaks_and_extra_comma_spaces(int width)
    {
        Format(Values.Replace("1, 'x'", "1   , 'x'"), Options(width), Choice("values.stackList", "on"), Choice("values.stackMode", "auto"));
        Format(Values, Options(width), Choice("values.stackList", "on"), Choice("values.stackMode", "auto"),
            Choice("values.breakAfterOpen", "always"), Choice("values.breakBeforeClose", "always"));
        Format("BEGIN " + Values + " END;", Options(width), Choice("using.breakBefore", "always"),
            Indent("using.keywordIndent", 1), Choice("values.stackRows", "on"), Choice("values.stackRowsMode", "auto"),
            Choice("values.stackList", "on"), Choice("values.stackMode", "auto"), Choice("values.breakAfterOpen", "always"));
    }

    [Theory]
    [InlineData("on", Basic)]
    [InlineData("join", Join)]
    public void Boolean_modes_and_operator_sides_are_independent(string group, string sql)
    {
        var all = Format(sql, Choice(group + ".wrapCondition", "both"));
        var and = Format(sql, Choice(group + ".wrapCondition", "and"));
        var or = Format(sql, Choice(group + ".wrapCondition", "or"));
        Assert.Contains("\nAND ", all);
        Assert.Contains("\nOR ", all);
        Assert.Contains("\nAND ", and);
        Assert.Contains("\nOR ", or);
        Assert.NotEqual(all, and);
        Assert.NotEqual(all, or);
        Assert.Contains(" AND ", Format(sql, Choice(group + ".wrapCondition", "none")));
        Assert.Equal(Format(sql), Format(sql, Choice(group + ".wrapCondition", "inherit")));
        var before = Format(sql, Choice(group + ".wrapBeforeOperator", "always"), Choice(group + ".wrapAfterOperator", "never"));
        var after = Format(sql, Choice(group + ".wrapBeforeOperator", "never"), Choice(group + ".wrapAfterOperator", "always"));
        Assert.Contains("\nAND ", before);
        Assert.Contains(" AND\n", after);
        Assert.NotEqual(before, after);
        var between = sql.Replace("s.a = 1", "s.a BETWEEN 1 AND 2").Replace("u.a = 1", "u.a BETWEEN 1 AND 2");
        Assert.Contains("BETWEEN 1 AND 2", Format(between, Choice(group + ".wrapCondition", "both")));
    }

    [Fact]
    public void Join_inheritance_does_not_replace_outer_merge_on_or_derived_query_rules()
    {
        var local = Choice("join.onBreakAfter", "never");
        var select = FullChoice("select.join.onBreakAfter", "always");
        var independent = Format(Join, local, select);
        var inherited = Format(Join, local, select, Bool("join.useSelectFormatting", true));
        Assert.Contains("ON s.id", independent);
        Assert.Contains("ON\ns.id", inherited);
        Assert.Contains("ON t.id", inherited);
        Assert.Equal(independent, Format(Join, local, select, Bool("join.useSelectFormatting", false)));
        Assert.Contains("ON\nt.id", Format(Join, Bool("join.useSelectFormatting", true), select, Choice("on.breakAfter", "always")));
        var derived = Join.Replace("dbo.U AS u", "(SELECT id, a FROM dbo.U) AS u");
        Assert.Contains("SELECT id, a FROM dbo.U", Format(derived, Bool("join.useSelectFormatting", true),
            FullChoice("select.join.breakAfter", "always")));
    }

    [Fact]
    public void Using_accepts_tables_queries_values_apply_and_parenthesized_join_sources()
    {
        var query = Basic.Replace("dbo.S AS s", "(SELECT id, a FROM dbo.S WHERE a = 1) AS s");
        Assert.Contains("USING\n(", Format(query, Choice("using.breakAfter", "always")));
        static string QueryBody(string text) => text.Substring(text.IndexOf("SELECT", StringComparison.Ordinal),
            text.IndexOf(") AS s", StringComparison.Ordinal) - text.IndexOf("SELECT", StringComparison.Ordinal));
        Assert.Equal(QueryBody(Format(query)), QueryBody(Format(query, Choice("on.breakAfter", "always"))));
        var nested = Join.Replace("dbo.S AS s INNER JOIN dbo.U AS u ON s.id = u.id AND u.a = 1 OR u.a = 2",
            "(dbo.S AS s INNER JOIN dbo.U AS u ON s.id = u.id) CROSS APPLY (VALUES (1, 2), (3, 4)) AS v(a, b)");
        var changed = Format(nested, Choice("join.breakBefore", "always"), Choice("values.stackRows", "on"));
        Assert.Contains("\nINNER JOIN", changed);
        Assert.Contains("\nCROSS APPLY", changed);
        Assert.Contains("),\n(3, 4)", changed);
        Format(Join.Replace("INNER JOIN", "INNER LOOP JOIN"), Choice("join.breakBefore", "always"));
    }

    [Fact]
    public void Joined_values_use_the_final_join_table_anchor_for_expression_indents_and_auto()
    {
        var sql = Join.Replace("dbo.U AS u", "(VALUES (1, 2), (3, 4)) AS u(id, a)");
        var changed = Format(sql, Choice("join.breakBefore", "always"), Indent("join.keywordIndent", 1),
            Choice("join.breakAfter", "always"), Indent("join.tableIndent", 1),
            Choice("values.breakAfterOpen", "always"), Indent("values.listIndent", 1), Choice("values.stackRows", "on"));
        Assert.Contains("\n        (VALUES (\n            1", changed);
        Format(sql, Options(16), Choice("join.breakBefore", "always"), Indent("join.keywordIndent", 1),
            Choice("join.breakAfter", "always"), Indent("join.tableIndent", 1), Choice("values.breakAfterOpen", "always"),
            Indent("values.listIndent", 1), Choice("values.stackList", "on"), Choice("values.stackMode", "auto"));
    }

    [Fact]
    public void Join_condition_indent_uses_the_on_line_after_structural_formatting()
    {
        var sql = Join.Replace(" OPTION (RECOMPILE)", string.Empty);
        Assert.Contains("ON\n        s.id", Format(sql, Choice("join.onBreakAfter", "always"), Indent("join.onConditionIndent", 1)));
    }

    [Fact]
    public void Source_rules_do_not_reformat_actions_output_options_or_other_statements()
    {
        var sql = Values.Replace("WHEN MATCHED THEN DELETE;",
            "WHEN MATCHED AND s.a = 'x' THEN UPDATE SET a = 1, b = 2 WHEN NOT MATCHED THEN INSERT (id, a) VALUES (s.id, s.a) OUTPUT inserted.id OPTION (RECOMPILE);");
        var tail = sql.Substring(sql.IndexOf("WHEN MATCHED", StringComparison.Ordinal));
        var changed = Format(sql, Choice("values.stackList", "on"), Choice("values.stackRows", "on"), Choice("on.breakAfter", "always"));
        Assert.EndsWith(tail, changed);
        const string other = "UPDATE dbo.T SET a = 1, b = 2; SELECT a FROM dbo.T; INSERT INTO dbo.T VALUES (1, 2);";
        Assert.Equal(Format(other), Format(other, Choice("values.stackList", "on"), Choice("on.breakAfter", "always")));
        var queryValues = Basic.Replace("dbo.S AS s", "(SELECT id, a FROM (VALUES (1, 'x'), (2, 'y')) AS q(id, a)) AS s");
        Assert.Equal(Format(queryValues), Format(queryValues, Choice("values.stackList", "on"), Choice("values.stackRows", "on")));
        Assert.Contains("USING\n@source", Format(Basic.Replace("dbo.S AS s", "@source AS s"), Choice("using.breakAfter", "always")));
        Assert.DoesNotContain("INTO", Format(Basic.Replace("MERGE INTO", "MERGE"), Choice("into.breakBefore", "always")));
    }

    [Fact]
    public void Nested_not_conditions_styles_transparency_and_signed_indents_are_stable()
    {
        var sql = "BEGIN " + Join.Replace("ON t.id = s.id", "ON NOT (t.id = s.id AND (\n s.a = 1 OR s.a = 2))") + " END;";
        Assert.Contains("\n        INNER JOIN", Format(sql, Choice("join.breakBefore", "always"), Indent("join.keywordIndent", 1)));
        Assert.Contains("\n    INNER JOIN", Format(sql, Choice("join.breakBefore", "always"), Indent("join.keywordIndent", 1, style: "absolute")));
        Assert.Contains("\nINNER JOIN", Format(sql, Choice("join.breakBefore", "always"), Indent("join.keywordIndent", 1, transparent: true)));
        Assert.Contains("(\n        s.a", Format(sql, Indent("on.nestedConditionIndent", 1)));
        Format(sql, Choice("on.wrapAfterOperator", "always"), Indent("on.nestedConditionIndent", -1),
            Choice("join.onBreakBefore", "always"), Indent("join.onKeywordIndent", 1));
        Assert.Contains("\n        USING", Format("BEGIN " + Basic + " END;", Options().With(indent: new IndentOptions(4, true)),
            Indent("using.keywordIndent", 1)));
    }

    [Fact]
    public void Hints_keep_nested_hint_parentheses_and_indent_each_existing_line()
    {
        var sql = Hints.Replace("HOLDLOCK, UPDLOCK", "HOLDLOCK,\nINDEX(ix_a, ix_b)");
        var changed = Format(sql, Choice("hints.breakBeforeOpen", "always"), Choice("hints.breakAfterOpen", "always"),
            Choice("hints.breakBeforeClose", "always"), Indent("hints.braceIndent", 1), Indent("hints.listIndent", 1));
        Assert.Contains("\n    (\n        HOLDLOCK,\n        INDEX(ix_a, ix_b)\n    )", changed);
    }

    [Fact]
    public void Cte_top_stored_code_and_comment_fallback_preserve_tokens_and_are_idempotent()
    {
        var sql = "WITH c AS (SELECT id, a FROM dbo.S) " + Basic.Replace("MERGE INTO", "MERGE TOP (5) INTO");
        Assert.Contains("TOP (5)\nINTO", Format(sql, Choice("into.breakBefore", "always")));
        var commented = Values.Replace("USING", "-- source\nUSING").Replace("ON t.id", "ON /* match */ t.id")
            .Replace(", 'x'", ", -- value\n 'x'");
        var changed = Format("BEGIN " + commented + " END;", Choice("using.breakBefore", "never"),
            Choice("values.stackList", "off"), Choice("on.breakAfter", "always"), Choice("values.stackRows", "on"),
            Indent("using.keywordIndent", 1), Indent("values.braceIndent", 1));
        Assert.Contains("-- source\n", changed);
        Assert.Contains("/* match */", changed);
        Assert.Contains("-- value\n", changed);
        Assert.Contains("'AND ON USING'", Format(Values.Replace("'x'", "'AND ON USING'"), Choice("values.stackList", "on")));
        var multiline = Values.Replace("'x'", "'a\nb'");
        Assert.Equal(multiline, Format(multiline, Choice("values.stackList", "on")));
        var quoted = Basic.Replace("dbo.T", "[a\nb]");
        Assert.Equal(quoted, Format(quoted, Choice("into.breakBefore", "always")));
        var invalid = Basic.TrimEnd(';');
        var result = new ScriptDomSqlFormatter().Format(invalid, Options().With(rules:
            new RuleOptions(RuleCatalog.Default).With("merge.on.breakAfter", RuleValue.FromChoice("always"))), new FormatRequest());
        Assert.False(result.ParseSucceeded);
        Assert.Equal(invalid, result.Text);
    }

    [Theory]
    [InlineData(DocLineEnding.CrLf, "\r\n")]
    [InlineData(DocLineEnding.Cr, "\r")]
    public void New_boundaries_use_configured_eol(DocLineEnding ending, string eol)
    {
        Assert.Contains("VALUES" + eol + "(", Format(Values, Options(ending: ending), Choice("values.breakAfterKeyword", "always")));
    }

    [Fact]
    public void Statement_scope_changes_only_the_requested_merge()
    {
        var options = Options().With(rules: new RuleOptions(RuleCatalog.Default).With("merge.into.breakBefore", RuleValue.FromChoice("always")));
        var result = new ScriptDomSqlFormatter().Format(Basic + "\n" + Basic, options,
            new FormatRequest(FormatScope.Statement, new SqlTextSpan(1, 0)));
        Assert.StartsWith("MERGE\nINTO", result.Text);
        Assert.EndsWith("\n" + Basic, result.Text);
    }

    [Fact]
    public void V2_round_trips_every_rule_v1_keeps_defaults_and_invalid_settings_are_rejected()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var descriptor in RuleCatalog.Default.Definitions.Values.Where(rule => IsHeaderRule(rule.Key)))
            rules = rules.With(descriptor.Key, descriptor.DefaultValue.Kind switch
            {
                RuleValueKind.Boolean => RuleValue.FromBoolean(true),
                RuleValueKind.Indent => RuleValue.FromIndent(new IndentRule(true, -2, false, "absolute", true)),
                _ => RuleValue.FromChoice(descriptor.Choices.Last())
            });
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(Options().With(rules: rules))).Rules.Overrides);
        Assert.Equal(Format(Basic), new ScriptDomSqlFormatter().Format(Basic,
            serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.Throws<ArgumentException>(() => rules.With("merge.values.stackRowsMode", RuleValue.FromChoice("unknown")));
        Assert.Throws<ArgumentException>(() => rules.With("merge.join.useSelectFormatting", RuleValue.FromChoice("on")));
        Assert.Throws<ArgumentException>(() => rules.With("merge.on.conditionIndent", RuleValue.FromIndent(new IndentRule(true, 33))));
        Assert.False(serializer.Parse("""{"version":2,"rules":{"merge.hints.spaceWithin":true}}""").Succeeded);
    }

    [Fact]
    public async Task Cli_loads_the_documented_merge_header_configuration()
    {
        const string json = """
        {"version":2,"rules":{
          "merge.into.breakBefore":"always",
          "merge.using.breakBefore":"always",
          "merge.on.breakBefore":"always",
          "merge.on.breakAfter":"always",
          "merge.on.conditionIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
          "merge.values.breakAfterKeyword":"always",
          "merge.values.braceIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
          "merge.values.stackRows":"on","merge.values.stackList":"off"
        }}
        """;
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-merge-header-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, Values);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal("MERGE\nINTO dbo.T AS t\nUSING (VALUES\n    (1, 'x'),\n    (2, 'y')) AS s(id, a)\nON\n    t.id = s.id WHEN MATCHED THEN DELETE;", output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static bool IsHeaderRule(string key) => new[] { "into", "hints", "using", "join", "on", "values" }
        .Any(group => key.StartsWith("merge." + group + ".", StringComparison.Ordinal));
    private static FormattingOptions Options(int width = 100, DocLineEnding ending = DocLineEnding.Lf) =>
        FormattingOptions.Default.With(general: new GeneralOptions(width, ending), keywords: new KeywordOptions(KeywordCase.Preserve));
    private static (string, RuleValue) Choice(string key, string value) => FullChoice("merge." + key, value);
    private static (string, RuleValue) FullChoice(string key, string value) => (key, RuleValue.FromChoice(value));
    private static (string, RuleValue) Bool(string key, bool value) => ("merge." + key, RuleValue.FromBoolean(value));
    private static (string, RuleValue) Indent(string key, int offset, bool onNewLineOnly = true,
        string style = "relative", bool transparent = false) => ("merge." + key, RuleValue.FromIndent(new IndentRule(true, offset, onNewLineOnly, style, transparent)));
    private static string Format(string sql, params (string Key, RuleValue Value)[] rules) => Format(sql, Options(), rules);
    private static string Format(string sql, FormattingOptions options, params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        options = options.With(rules: values);
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded, string.Join("; ", first.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        var parser = new ScriptDomSqlParser();
        var before = parser.Parse(sql, SqlDialectVersion.Auto, CancellationToken.None);
        var after = parser.Parse(first.Text, SqlDialectVersion.Auto, CancellationToken.None);
        Assert.True(after.ParseSucceeded);
        static IEnumerable<string> Tokens(SqlParseResult parsed) => parsed.Tokens.Where(token =>
            token.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).Select(token => token.Text);
        Assert.Equal(Tokens(before), Tokens(after));
        return first.Text;
    }
}
