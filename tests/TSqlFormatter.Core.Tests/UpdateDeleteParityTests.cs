using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class UpdateDeleteParityTests
{
    private const string Update = "UPDATE t SET a = 1, longer = 2 OUTPUT inserted.a, inserted.longer FROM dbo.T AS t INNER JOIN dbo.S AS s ON t.a = s.a AND s.b = 1 OR t.b = 2 WHERE t.a = 1 AND t.b = 2 OR t.b = 3 OPTION (RECOMPILE, MAXDOP 1);";
    private const string Delete = "DELETE FROM t OUTPUT deleted.a, deleted.longer FROM dbo.T AS t INNER JOIN dbo.S AS s ON t.a = s.a AND s.b = 1 OR t.b = 2 WHERE t.a = 1 AND t.b = 2 OR t.b = 3 OPTION (RECOMPILE, MAXDOP 1);";

    [Fact]
    public void Catalog_and_ledger_cover_all_update_delete_paths()
    {
        var keys = RuleCatalog.Default.Definitions.Keys.Where(key => key.StartsWith("update.") || key.StartsWith("delete.")).ToArray();
        Assert.Equal(45, keys.Count(key => key.StartsWith("update.")));
        Assert.Equal(41, keys.Count(key => key.StartsWith("delete.")));
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "coverage.tsv"))
            .Skip(1).Select(line => line.Split('\t')).Where(row => row[1] == "SC-15").ToArray();
        Assert.Equal(156, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal("covered", row[2]);
            Assert.Contains(row[3], keys);
            Assert.StartsWith(nameof(UpdateDeleteParityTests) + ".", row[4]);
            Assert.NotEqual("-", row[5]);
        });
        Assert.Equal(keys.OrderBy(key => key), rows.Select(row => row[3]).Distinct().OrderBy(key => key));
    }

    public static IEnumerable<object[]> Boundaries()
    {
        foreach (var prefix in new[] { "update", "delete" })
        {
            foreach (var (key, next) in new[] {
                         ("target.breakBefore", "t"), ("from.breakBefore", "FROM"), ("from.breakAfter", "dbo.T"),
                         ("join.breakBefore", "INNER JOIN"), ("join.breakAfter", "dbo.S"),
                         ("join.onBreakBefore", "ON"), ("join.onBreakAfter", "t.a"),
                         ("where.breakBefore", "WHERE"), ("where.breakAfter", "t.a"),
                         ("output.breakBefore", "OUTPUT"), ("output.breakAfter", prefix == "update" ? "inserted.a" : "deleted.a"),
                         ("option.breakBefore", "OPTION"), ("option.breakAfter", "(") })
                yield return new object[] { prefix + "." + key, next };
        }
        yield return new object[] { "update.set.breakBefore", "SET" };
        yield return new object[] { "update.set.breakAfter", "a" };
        yield return new object[] { "delete.target.breakBeforeFrom", "FROM" };
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Every_boundary_offers_always_never_and_inherit(string key, string next)
    {
        var sql = Source(key);
        var expanded = Format(sql, Choice(key, "always"));
        var compact = Format(sql, Choice(key, "never"));
        Assert.NotEqual(expanded, compact);
        Assert.Contains("\n" + next, expanded);
        Assert.Equal(Format(sql), Format(sql, Choice(key, "inherit")));
    }

    public static IEnumerable<object[]> Indents()
    {
        foreach (var prefix in new[] { "update", "delete" })
        {
            foreach (var (key, boundary, next) in new[] {
                         ("target.indent", "target.breakBefore", "t"),
                         ("from.keywordIndent", "from.breakBefore", "FROM"), ("from.listIndent", "from.breakAfter", "dbo.T"),
                         ("join.keywordIndent", "join.breakBefore", "INNER JOIN"), ("join.tableIndent", "join.breakAfter", "dbo.S"),
                         ("join.onKeywordIndent", "join.onBreakBefore", "ON"), ("join.onConditionIndent", "join.onBreakAfter", "t.a"),
                         ("join.nestedConditionIndent", "join.wrapAfterOperator", "s.b"),
                         ("where.keywordIndent", "where.breakBefore", "WHERE"), ("where.conditionIndent", "where.breakAfter", "t.a"),
                         ("where.nestedConditionIndent", "where.wrapAfterOperator", "t.b"),
                         ("output.keywordIndent", "output.breakBefore", "OUTPUT"),
                         ("output.listIndent", "output.breakAfter", prefix == "update" ? "inserted.a" : "deleted.a"),
                         ("option.keywordIndent", "option.breakBefore", "OPTION"), ("option.hintsIndent", "option.breakAfter", "(") })
                yield return new object[] { prefix + "." + key, prefix + "." + boundary, next };
        }
        yield return new object[] { "update.set.keywordIndent", "update.set.breakBefore", "SET" };
        yield return new object[] { "update.set.listIndent", "update.set.breakAfter", "a" };
        yield return new object[] { "delete.target.fromKeywordIndent", "delete.target.breakBeforeFrom", "FROM" };
    }

    [Theory]
    [MemberData(nameof(Indents))]
    public void Every_indent_targets_a_real_boundary_and_honors_enabled_offset_and_newline(string key, string boundary, string next)
    {
        var sql = Source(key);
        var one = Format(sql, Choice(boundary, "always"), Indent(key, 1));
        var two = Format(sql, Choice(boundary, "always"), Indent(key, 2));
        Assert.Contains("\n    " + next, one);
        Assert.Contains("\n        " + next, two);
        Assert.NotEqual(one, two);
        var disabled = (key, RuleValue.FromIndent(new IndentRule(false, 2, true)));
        Assert.Equal(Format(sql, Choice(boundary, "always")), Format(sql, Choice(boundary, "always"), disabled));
        Assert.Equal(Format(sql, Choice(boundary, "never")), Format(sql, Choice(boundary, "never"), Indent(key, 1)));
        Assert.Contains("     " + next, Format(sql, Choice(boundary, "never"), Indent(key, 1, false)));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public void From_inheritance_uses_select_from_and_join_only_when_enabled(string prefix)
    {
        var sql = Source(prefix);
        var rules = new[] { Choice("select.from.breakAfter", "always"), Choice("select.join.onBreakAfter", "always"),
            Choice(prefix + ".from.breakAfter", "never"), Choice(prefix + ".join.onBreakAfter", "never"),
            Choice(prefix + ".where.breakAfter", "never"), Choice("select.where.breakAfter", "always") };
        var independent = Format(sql, rules);
        var inherited = Format(sql, rules.Append(Bool(prefix + ".from.useSelectFormatting", true)).ToArray());
        Assert.Contains("FROM dbo.T", independent);
        Assert.Contains("ON t.a", independent);
        Assert.Contains("FROM\ndbo.T", inherited);
        Assert.Contains("ON\nt.a", inherited);
        Assert.Contains("WHERE t.a", inherited);
        Assert.Equal(independent, Format(sql, rules.Append(Bool(prefix + ".from.useSelectFormatting", false)).ToArray()));
    }

    [Theory]
    [InlineData("update", "from")]
    [InlineData("delete", "from")]
    [InlineData("update", "output")]
    [InlineData("delete", "output")]
    [InlineData("update", "set")]
    public void Each_list_supports_stacking_compaction_auto_and_global_commas(string prefix, string group)
    {
        var sql = Source(prefix).Replace(" INNER JOIN dbo.S AS s ON t.a = s.a AND s.b = 1 OR t.b = 2", ", dbo.S AS s");
        var key = prefix + "." + group;
        var vertical = Format(sql, Choice(key + ".stackList", "on"));
        var flat = Format(sql, Choice(key + ".stackList", "off"));
        Assert.NotEqual(vertical, flat);
        Assert.Equal(flat, Format(sql, Choice(key + ".stackList", "on"), Choice(key + ".stackMode", "auto")));
        Assert.Equal(vertical, Format(sql, Options(width: 6), Choice(key + ".stackList", "on"), Choice(key + ".stackMode", "auto")));
        Assert.Equal(Format(sql), Format(sql, Choice(key + ".stackMode", "auto")));
        var leading = Format(sql, Choice(key + ".stackList", "on"), Choice("stackedList.commaPlacement", "leading"),
            Choice("stackedList.spaceAfterLeadingComma", "remove"), Indent(key + ".listIndent", 1));
        Assert.Contains("\n    ,", leading);
        Assert.DoesNotContain("\n    , ", leading);
        Assert.Contains(",\n    ", Format(sql, Choice(key + ".stackList", "on"), Indent(key + ".listIndent", 1)));
        var existing = sql.Replace(", ", ",\n");
        Assert.Contains(",\n    ", Format(existing, Indent(key + ".listIndent", 1)));
    }

    [Theory]
    [InlineData("update", "join")]
    [InlineData("delete", "join")]
    [InlineData("update", "where")]
    [InlineData("delete", "where")]
    public void Boolean_modes_and_operator_sides_are_independent(string prefix, string group)
    {
        var key = prefix + "." + group;
        var sql = Source(prefix);
        var all = Format(sql, Choice(key + ".wrapCondition", "both"));
        Assert.Contains("\nAND ", all);
        Assert.Contains("\nOR ", all);
        var and = Format(sql, Choice(key + ".wrapCondition", "and"));
        Assert.Contains("\nAND ", and);
        var or = Format(sql, Choice(key + ".wrapCondition", "or"));
        Assert.Contains("\nOR ", or);
        Assert.NotEqual(all, and);
        Assert.NotEqual(all, or);
        Assert.Equal(Format(sql), Format(sql, Choice(key + ".wrapCondition", "none")));
        var before = Format(sql, Choice(key + ".wrapBeforeOperator", "always"), Choice(key + ".wrapAfterOperator", "never"));
        var after = Format(sql, Choice(key + ".wrapBeforeOperator", "never"), Choice(key + ".wrapAfterOperator", "always"));
        Assert.Contains("\nAND ", before);
        Assert.Contains(" AND\n", after);
        Assert.NotEqual(before, after);
        Assert.Equal(Format(sql), Format(sql, Choice(key + ".wrapBeforeOperator", "never"), Choice(key + ".wrapAfterOperator", "never")));
        var between = sql.Replace("t.a = 1", "t.a BETWEEN 1 AND 2").Replace("t.a = s.a", "t.a BETWEEN 1 AND 2");
        Assert.Contains("BETWEEN 1 AND 2", Format(between, Choice(key + ".wrapCondition", "both")));
    }

    [Fact]
    public void Vertical_set_retains_alignment_but_horizontal_set_uses_single_spaces()
    {
        const string sql = "UPDATE dbo.T SET a = 1, longer = 2;";
        var options = Options().With(alignment: new AlignmentOptions(setAssignments: true));
        var vertical = Format(sql, options, Choice("update.set.stackList", "on"));
        Assert.Contains("a      = 1,\n    longer = 2", vertical);
        var flat = Format(sql, options, Choice("update.set.stackList", "off"), Choice("update.set.breakAfter", "never"));
        Assert.Contains("SET a = 1, longer = 2", flat);
        Assert.Equal(flat, Format(sql, options, Choice("update.set.stackList", "on"), Choice("update.set.stackMode", "auto"),
            Choice("update.set.breakAfter", "never")));
        Assert.Contains("a      = 1\n    , longer = 2", Format(sql, options, Choice("update.set.stackList", "on"),
            Choice("stackedList.commaPlacement", "leading")));
    }

    [Fact]
    public void Header_from_is_distinct_from_delete_source_from()
    {
        var changed = Format(Delete, Choice("delete.target.breakBeforeFrom", "always"),
            Indent("delete.target.fromKeywordIndent", 1), Choice("delete.from.breakBefore", "always"),
            Indent("delete.from.keywordIndent", 2));
        Assert.StartsWith("DELETE\n    FROM t", changed);
        Assert.Contains("\n        FROM dbo.T", changed);
        Assert.StartsWith("DELETE\nt", Format("DELETE t FROM dbo.T AS t;", Choice("delete.target.breakBefore", "always")));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public void Output_into_and_option_hint_block_preserve_all_tokens(string prefix)
    {
        var sql = Source(prefix).Replace(" FROM dbo.T", " INTO @log (a, longer) FROM dbo.T");
        var changed = Format(sql, Choice(prefix + ".output.stackList", "on"), Choice(prefix + ".output.breakAfter", "always"),
            Choice(prefix + ".option.breakBefore", "always"), Choice(prefix + ".option.breakAfter", "always"),
            Indent(prefix + ".option.hintsIndent", 1));
        Assert.Contains(" INTO @log (a, longer)", changed);
        Assert.Contains("\nOPTION\n    (RECOMPILE, MAXDOP 1)", changed);
        var bothOutputs = sql.Replace(" INTO @log (a, longer)", " INTO @log (a, longer) OUTPUT 'second', 2");
        Assert.Contains("OUTPUT\n'second',\n2", Format(bothOutputs, Choice(prefix + ".output.breakAfter", "always"),
            Choice(prefix + ".output.stackList", "on")));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public void Scoped_rules_handle_top_apply_nested_join_and_stored_code_without_touching_select(string prefix)
    {
        var sql = Source(prefix).Replace(prefix == "update" ? "UPDATE t" : "DELETE FROM t",
            prefix == "update" ? "UPDATE TOP (5) t" : "DELETE TOP (5) FROM t");
        Assert.Contains("TOP (5)", Format(sql, Choice(prefix + ".target.breakBefore", "always")));
        var nested = Source(prefix).Replace("dbo.T AS t INNER JOIN dbo.S AS s ON t.a = s.a AND s.b = 1 OR t.b = 2",
            "(dbo.T AS t INNER JOIN dbo.S AS s ON t.a = s.a) CROSS APPLY (SELECT a FROM dbo.U) AS u");
        var changed = Format(nested, Choice(prefix + ".join.breakBefore", "always"), Choice(prefix + ".from.breakAfter", "always"));
        Assert.Contains("\nINNER JOIN", changed);
        Assert.Contains("\nCROSS APPLY", changed);
        Assert.Contains("SELECT a FROM dbo.U", changed);
        var stored = Format("BEGIN " + Source(prefix) + " END;", Choice(prefix + ".where.breakAfter", "always"));
        Assert.Contains("WHERE\n", stored);
        var cte = Format("WITH c AS (SELECT a FROM dbo.U) " + Source(prefix), Choice(prefix + ".from.breakAfter", "always"));
        Assert.Contains("WITH c AS", cte);
        Assert.Equal(Format("SELECT a FROM dbo.U;"), Format("SELECT a FROM dbo.U;", Choice(prefix + ".from.breakAfter", "always")));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public void Nested_not_predicates_and_relative_absolute_transparent_indents_are_stable(string prefix)
    {
        var sql = "BEGIN " + Source(prefix).Replace("WHERE t.a = 1 AND t.b = 2 OR t.b = 3",
            "WHERE NOT (t.a = 1 AND t.b = 2) OR t.b = 3") + " END;";
        var rules = new[] { Choice(prefix + ".where.wrapAfterOperator", "always"),
            Choice(prefix + ".join.breakBefore", "always") };
        var relative = Format(sql, rules.Append(Indent(prefix + ".join.keywordIndent", 1)).ToArray());
        var absolute = Format(sql, rules.Append(Indent(prefix + ".join.keywordIndent", 1, style: "absolute")).ToArray());
        var transparent = Format(sql, rules.Append(Indent(prefix + ".join.keywordIndent", 1, transparent: true)).ToArray());
        Assert.Contains("\n        INNER JOIN", relative);
        Assert.Contains("\n    INNER JOIN", absolute);
        Assert.Contains("\nINNER JOIN", transparent);
        Assert.Contains("AND\n", relative);
        Format(sql, Choice(prefix + ".where.wrapAfterOperator", "always"), Indent(prefix + ".where.nestedConditionIndent", -1));
        var multiline = Source(prefix).Replace("WHERE t.a = 1", "WHERE (\n t.a = 1").Replace("OR t.b = 3 OPTION", "OR t.b = 3) OPTION");
        Assert.Contains("(\n    t.a", Format(multiline, Indent(prefix + ".where.nestedConditionIndent", 1)));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public void Comments_literals_and_multiline_tokens_remain_intact(string prefix)
    {
        var sql = Source(prefix).Replace("WHERE t.a = 1", "WHERE -- condition\n t.a = 1")
            .Replace("AND t.b = 2", "AND /* operand */ t.b = 2");
        var changed = Format(sql, Choice(prefix + ".where.breakAfter", "never"), Choice(prefix + ".where.wrapCondition", "both"));
        Assert.Contains("-- condition\n", changed);
        Assert.Contains("/* operand */", changed);
        Assert.Contains("'AND FROM WHERE'", Format(Source(prefix).Replace("t.a = 1", "t.a = 'AND FROM WHERE'"),
            Choice(prefix + ".where.wrapCondition", "both")));
        var multiline = Source(prefix).Replace("t.a = 1", "t.a = 'a\nb'");
        Assert.Equal(multiline, Format(multiline, Choice(prefix + ".where.wrapCondition", "both")));
        Format(Source(prefix).Replace("INNER JOIN", "-- join\nINNER JOIN"), Choice(prefix + ".join.breakBefore", "never"));
    }

    [Fact]
    public void Compound_assignments_variable_targets_cursor_where_and_join_hints_use_safe_boundaries()
    {
        Assert.Contains("SET\na += 1,\nlonger -= 2", Format("UPDATE TOP (@n) @t SET a += 1, longer -= 2;",
            Choice("update.set.breakAfter", "always"), Choice("update.set.stackList", "on")));
        Assert.Contains("WHERE\nCURRENT OF cur", Format("DELETE FROM @t WHERE CURRENT OF cur;",
            Choice("delete.where.breakAfter", "always")));
        Assert.Contains("\nINNER LOOP JOIN", Format("UPDATE t SET a = 1 FROM dbo.T AS t INNER LOOP JOIN dbo.S AS s ON t.a = s.a OPTION (RECOMPILE);",
            Choice("update.join.breakBefore", "always")));
        Assert.Contains("dbo.S", Format(Delete, Bool("delete.from.useSelectFormatting", true),
            Choice("select.join.breakAfter", "always"), Indent("select.join.tableIndent", 1)));
        var invalid = "UPDATE dbo.T SET a = ;";
        var options = Options().With(rules: new RuleOptions(RuleCatalog.Default).With("update.set.breakAfter", RuleValue.FromChoice("always")));
        var result = new ScriptDomSqlFormatter().Format(invalid, options, new FormatRequest());
        Assert.False(result.ParseSucceeded);
        Assert.Equal(invalid, result.Text);
    }

    [Fact]
    public void Indent_only_rules_move_inherited_leading_commas_and_handle_tab_indented_code()
    {
        var changed = Format("UPDATE dbo.T SET a = 1, longer = 2;", Choice("stackedList.commaPlacement", "leading"),
            Indent("update.set.listIndent", 2));
        Assert.Contains("\n        a = 1\n        , longer = 2", changed);
        var options = Options().With(indent: new IndentOptions(4, useTabs: true));
        Assert.Contains("\n        SET", Format("BEGIN UPDATE dbo.T SET a = 1; END;", options,
            Indent("update.set.keywordIndent", 1)));
    }

    [Fact]
    public void Local_policies_remain_stable_when_a_comment_forces_structural_fallback()
    {
        const string sql = "BEGIN UPDATE t SET a = 1, longer = 2 FROM dbo.T AS t INNER JOIN dbo.S AS s ON t.a = s.a AND s.b = 1 WHERE -- filter\n t.a = 1 AND (\n t.b = 2 OR t.b = 3); END;";
        Format(sql, Choice("update.set.breakBefore", "always"), Indent("update.set.keywordIndent", 1),
            Choice("update.set.breakAfter", "always"), Indent("update.set.listIndent", 1), Choice("update.set.stackList", "on"),
            Choice("update.from.breakBefore", "always"), Indent("update.from.keywordIndent", 1),
            Choice("update.from.breakAfter", "always"), Indent("update.from.listIndent", 1),
            Choice("update.join.breakBefore", "always"), Indent("update.join.keywordIndent", 1),
            Choice("update.join.onBreakBefore", "always"), Indent("update.join.onKeywordIndent", 1),
            Choice("update.join.onBreakAfter", "always"), Indent("update.join.onConditionIndent", 1),
            Choice("update.join.wrapAfterOperator", "always"), Indent("update.join.nestedConditionIndent", 1),
            Choice("update.where.breakBefore", "always"), Indent("update.where.keywordIndent", 1),
            Choice("update.where.wrapAfterOperator", "always"), Indent("update.where.nestedConditionIndent", 1));
    }

    [Fact]
    public void Update_and_delete_rules_are_independent_and_statement_scope_leaves_the_rest_alone()
    {
        var options = Options().With(rules: new RuleOptions(RuleCatalog.Default).With("update.where.breakAfter", RuleValue.FromChoice("always")));
        var result = new ScriptDomSqlFormatter().Format(Update + "\n" + Delete, options,
            new FormatRequest(FormatScope.Statement, new SqlTextSpan(1, 0)));
        Assert.Contains("WHERE\n", result.Text);
        Assert.EndsWith("\n" + Delete, result.Text);
        Assert.Equal(Format(Delete), Format(Delete, Choice("update.where.breakAfter", "always")));
    }

    [Theory]
    [InlineData(DocLineEnding.CrLf, "\r\n")]
    [InlineData(DocLineEnding.Cr, "\r")]
    public void New_boundaries_use_configured_eol(DocLineEnding ending, string eol)
    {
        Assert.Contains("SET" + eol + "a", Format(Update, Options(ending: ending), Choice("update.set.breakAfter", "always")));
    }

    [Fact]
    public void V2_round_trips_every_rule_and_v1_preserves_defaults_and_invalid_rules_are_rejected()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var descriptor in RuleCatalog.Default.Definitions.Values.Where(rule => rule.Key.StartsWith("update.") || rule.Key.StartsWith("delete.")))
            rules = rules.With(descriptor.Key, descriptor.DefaultValue.Kind switch
            {
                RuleValueKind.Boolean => RuleValue.FromBoolean(true),
                RuleValueKind.Indent => RuleValue.FromIndent(new IndentRule(true, -2, false, "absolute", true)),
                _ => RuleValue.FromChoice(descriptor.Choices.Last())
            });
        var options = Options().With(rules: rules);
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(options)).Rules.Overrides);
        Assert.Equal(Format(Update), new ScriptDomSqlFormatter().Format(Update,
            serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.Throws<ArgumentException>(() => rules.With("update.set.stackMode", RuleValue.FromChoice("unknown")));
        Assert.Throws<ArgumentException>(() => rules.With("delete.from.useSelectFormatting", RuleValue.FromChoice("on")));
        Assert.Throws<ArgumentException>(() => rules.With("delete.target.indent", RuleValue.FromIndent(new IndentRule(true, 33, true))));
    }

    [Fact]
    public async Task Cli_loads_the_documented_update_delete_configuration()
    {
        const string json = """
        {"version":2,"rules":{
          "update.set.breakAfter":"never","update.set.stackList":"off",
          "update.where.breakBefore":"always","update.where.breakAfter":"always",
          "update.where.conditionIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
          "delete.from.useSelectFormatting":true,"select.join.onBreakAfter":"always",
          "delete.output.breakAfter":"always","delete.output.stackList":"on"
        }}
        """;
        const string sql = "UPDATE dbo.T SET a = 1, longer = 2 WHERE a = 1;";
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-update-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, sql);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal("UPDATE dbo.T\nSET a = 1, longer = 2\nWHERE\n    a = 1;", output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Source(string key) => key.StartsWith("update") ? Update : Delete;
    private static (string, RuleValue) Choice(string key, string value) => (key, RuleValue.FromChoice(value));
    private static (string, RuleValue) Bool(string key, bool value) => (key, RuleValue.FromBoolean(value));
    private static (string, RuleValue) Indent(string key, int offset, bool onNewLineOnly = true,
        string style = "relative", bool transparent = false) => (key, RuleValue.FromIndent(new IndentRule(true, offset, onNewLineOnly, style, transparent)));
    private static FormattingOptions Options(int width = 100, DocLineEnding ending = DocLineEnding.Lf) =>
        FormattingOptions.Default.With(general: new GeneralOptions(width, ending), keywords: new KeywordOptions(KeywordCase.Preserve));
    private static string Format(string sql, params (string Key, RuleValue Value)[] rules) => Format(sql, Options(), rules);
    private static string Format(string sql, FormattingOptions options, params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        var formatter = new ScriptDomSqlFormatter();
        options = options.With(rules: values);
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
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
