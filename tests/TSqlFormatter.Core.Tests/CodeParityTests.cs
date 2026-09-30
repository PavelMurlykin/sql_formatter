using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using static TSqlFormatter.Core.Tests.ParityTestSupport;

namespace TSqlFormatter.Core.Tests;

public sealed class CodeParityTests
{
    private const string Block = "BEGIN SET @a = 1; SET @b = 2; END;";
    private const string If = "IF\n@a = 1 AND (@b = 2 OR @b = 3) BEGIN SET @a = 2; END ELSE BEGIN SET @a = 3; END;";
    private const string While = "WHILE\n@a = 1 AND (@b = 2 OR @b = 3) BEGIN SET @a = 2; END;";
    private const string Handler = "BEGIN TRY SET @a = 1; END TRY BEGIN CATCH THROW; END CATCH";

    [Fact]
    public void Ledger_covers_all_code_paths_and_independent_styles()
    {
        CheckLedger("SC-20", 47, nameof(CodeParityTests));
        Assert.Equal(27, Ledger("SC-20").Select(r => r[3]).Distinct().Count());
    }
    public static IEnumerable<object[]> Boundaries() => new[]
    {
        new object[] { "breakAfterBegin", Block }, new object[] { "breakBeforeEnd", Block },
        new object[] { "separateStatements", "SET @a = 1; SET @b = 2;" },
        new object[] { "block.breakBeforeCatch", Handler }, new object[] { "if.breakAfterCondition", If },
        new object[] { "if.breakBeforeElse", If }, new object[] { "if.breakAfterElse", If },
        new object[] { "if.wrapBeforeOperator", If }, new object[] { "if.wrapAfterOperator", If },
        new object[] { "while.breakAfterCondition", While }, new object[] { "while.wrapBeforeOperator", While },
        new object[] { "while.wrapAfterOperator", While }
    };
    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Every_boundary_supports_distinct_modes(string key, string sql)
    {
        var expanded = Format(sql, Choice("code." + key, "always"));
        Assert.NotEqual(expanded, Format(sql, Choice("code." + key, "never")));
        Assert.Equal(Format(sql), Format(sql, Choice("code." + key, "inherit")));
    }
    public static IEnumerable<object[]> Indents() => new[]
    {
        new object[] { "block.bodyIndent", Block, "SET", "code.breakAfterBegin" },
        new object[] { "transaction.bodyIndent", "BEGIN TRAN; SET @a = 1; COMMIT TRAN;", "SET", "code.separateStatements" },
        new object[] { "if.keywordIndent", If, "BEGIN", "code.if.breakAfterCondition" },
        new object[] { "if.bodyIndent", If, "SET", "code.breakAfterBegin" },
        new object[] { "if.nestedConditionIndent", If, "@b", "code.if.wrapAfterOperator" },
        new object[] { "while.keywordIndent", While, "BEGIN", "code.while.breakAfterCondition" },
        new object[] { "while.bodyIndent", While, "SET", "code.breakAfterBegin" },
        new object[] { "while.nestedConditionIndent", While, "@b", "code.while.wrapAfterOperator" }
    };
    [Theory]
    [MemberData(nameof(Indents))]
    public void Every_body_keyword_or_nested_indent_honors_native_fields(string key, string sql, string token, string boundary)
    {
        key = "code." + key;
        Assert.Contains("\n    " + token, Format(sql, Choice(boundary, "always"), Indent(key)));
        Assert.Contains("\n        " + token, Format(sql, Choice(boundary, "always"), Indent(key, 2)));
        Assert.Equal(Format(sql, Choice(boundary, "always")), Format(sql, Choice(boundary, "always"),
            (key, RuleValue.FromIndent(new IndentRule(false, 2)))));
        Assert.Contains("\n" + token, Format(sql, Choice(boundary, "always"), Indent(key, transparent: true)));
        Format(sql, Choice(boundary, "always"), Indent(key, -1, style: "absolute"));
        Format(sql, Choice(boundary, "never"), Indent(key, newlineOnly: false));
    }

    [Theory]
    [InlineData("if", If, "IF")]
    [InlineData("while", While, "WHILE")]
    public void Condition_indents_and_boolean_wrapping_are_independent(string prefix, string sql, string keyword)
    {
        var key = "code." + prefix + ".conditionIndent";
        Assert.Contains(keyword + "\n    @a", Format(sql, Indent(key)));
        Assert.Contains(keyword + "\n        @a", Format(sql, Indent(key, 2)));
        Assert.Contains(keyword + "\n@a", Format(sql, Indent(key, transparent: true)));
        Assert.Contains(keyword + "     @a", Format(sql.Replace(keyword + "\n", keyword + " "), Indent(key, newlineOnly: false)));
        Assert.NotEqual(Format(sql, Choice("code." + prefix + ".wrapCondition", "and")),
            Format(sql, Choice("code." + prefix + ".wrapCondition", "or")));
        Assert.Contains("\nAND", Format(sql, Choice("code." + prefix + ".wrapCondition", "both")));
        Assert.DoesNotContain("\nOR", Format(sql, Choice("code." + prefix + ".wrapCondition", "none")));
    }

    [Theory]
    [InlineData("block", Block)]
    [InlineData("if", If)]
    [InlineData("while", While)]
    public void Blank_lines_surround_only_requested_constructs(string prefix, string sql)
    {
        var script = "SET @z = 0;\n" + sql + "\nSET @z = 1;";
        var expanded = Format(script, ("code." + prefix + ".blankLinesAround", RuleValue.FromBoolean(true)));
        Assert.Contains(";\n\n", expanded);
        Assert.Contains("\n\nSET @z = 1", expanded);
        Assert.NotEqual(expanded, Format(script, ("code." + prefix + ".blankLinesAround", RuleValue.FromBoolean(false))));
    }

    [Fact]
    public void Nested_control_flow_transactions_try_catch_and_comments_are_stable()
    {
        var rules = new[] { Choice("code.separateStatements", "always"), Choice("code.breakAfterBegin", "always"),
            Choice("code.breakBeforeEnd", "always"), Indent("code.block.bodyIndent"), Indent("code.transaction.bodyIndent"),
            Choice("code.if.breakAfterCondition", "always"), Choice("code.if.breakBeforeElse", "always"),
            Choice("code.if.breakAfterElse", "always"), Indent("code.if.keywordIndent"), Indent("code.if.bodyIndent", 2),
            Choice("code.if.wrapCondition", "both"), Indent("code.if.nestedConditionIndent"),
            Choice("code.while.breakAfterCondition", "always"), Indent("code.while.keywordIndent", 2), Indent("code.while.bodyIndent"),
            Choice("code.block.breakBeforeCatch", "always") };
        Format("BEGIN TRAN; " + If.Replace("SET @a = 2;", While) + " " + Handler + " COMMIT TRAN;", rules);
        Format(Handler + ";", rules);
        Format("BEGIN " + If.Replace("SET @a = 2;", While) + " END", rules);
        Format("IF @a = 1 WHILE @b = 1 SET @a = 2; ELSE SET @a = 3;", rules);
        Format("BEGIN TRAN; SET @a = 1; BEGIN TRAN; SET @b = 2; COMMIT; SET @c = 3; ROLLBACK;", rules);
        Format("BEGIN TRY -- keep\nSET @a = 1; END TRY /* keep */ BEGIN CATCH THROW; END CATCH;", rules);
        Format("BEGIN TRAN; SET @a = 1;", rules); // unmatched transaction: no speculative body indentation
    }

    [Fact]
    public void V2_round_trips_all_rules_v1_defaults_and_invalid_values_are_checked()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var key in Ledger("SC-20").Select(r => r[3]).Distinct())
            rules = rules.With(key, RuleCatalog.Default.Definitions[key].DefaultValue);
        Assert.Equal(rules.Overrides, serializer.Deserialize(serializer.Serialize(Options().With(rules: rules))).Rules.Overrides);
        Assert.Equal(Format(If), new ScriptDomSqlFormatter().Format(If,
            serializer.Deserialize("""{"version":1,"keywords":{"case":"preserve"}}"""), new FormatRequest()).Text);
        Assert.False(serializer.Parse("""{"version":2,"rules":{"code.if.wrapCondition":"unknown"}}""").Succeeded);
    }

    [Fact]
    public void Different_if_while_styles_and_nested_transaction_levels_have_observable_effects()
    {
        var sql = "BEGIN TRAN; SET @a = 1; BEGIN TRAN; SET @b = 2; COMMIT; SET @c = 3; COMMIT;";
        var nested = Format(sql, Choice("code.separateStatements", "always"), Indent("code.transaction.bodyIndent"));
        Assert.Contains("\n    BEGIN TRAN;\n        SET @b", nested);
        Assert.Contains("\n    COMMIT;\n    SET @c", nested);
        Assert.EndsWith("\nCOMMIT;", nested);
        Assert.DoesNotContain("\n        SET @b", Format(sql, Choice("code.separateStatements", "always"),
            Indent("code.transaction.bodyIndent", style: "absolute")));
        var styles = Format(If.Replace("SET @a = 2;", While), Choice("code.breakAfterBegin", "always"),
            Choice("code.if.breakAfterCondition", "always"), Choice("code.while.breakAfterCondition", "always"),
            Indent("code.if.bodyIndent", 2), Indent("code.while.bodyIndent"));
        Assert.Contains("\n        WHILE", styles);
        Assert.Contains("\n            SET @a = 2", styles);
        foreach (var (prefix, snippet) in new[] { ("if", If), ("while", While) })
        {
            var boundary = Choice("code." + prefix + ".breakAfterCondition", "never");
            Assert.NotEqual(Format(snippet, boundary), Format(snippet, boundary,
                Indent("code." + prefix + ".keywordIndent", newlineOnly: false)));
            var bodyBoundary = Choice("code.breakAfterBegin", "never");
            Assert.NotEqual(Format(snippet, bodyBoundary), Format(snippet, bodyBoundary,
                Indent("code." + prefix + ".bodyIndent", newlineOnly: false)));
        }
    }

    [Fact]
    public async Task Cli_scopes_and_invalid_inputs_follow_the_shared_code_configuration()
    {
        const string json = """
        {"version":2,"rules":{"code.separateStatements":"always","code.breakAfterBegin":"always","code.breakBeforeEnd":"always",
        "code.if.breakAfterCondition":"always","code.if.bodyIndent":{"enabled":true,"offset":2,"onNewLineOnly":true,"style":"relative","transparent":false},
        "code.while.bodyIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false}}}
        """;
        var options = new SqlFormatterConfigurationSerializer().Deserialize(json);
        var formatter = new ScriptDomSqlFormatter();
        var scoped = formatter.Format(Block + "\n" + If, options,
            new FormatRequest(FormatScope.Selection, new TSqlFormatter.Core.Parsing.SqlTextSpan(0, Block.Length)));
        Assert.EndsWith("\n" + If, scoped.Text);
        foreach (var sql in new[] { "IF incomplete", "BEGIN PRINT 'a\nb'; END" })
            Assert.Equal(sql, formatter.Format(sql, options, new FormatRequest()).Text);
        var directory = Path.Combine(Path.GetTempPath(), "tsqlformatter-code-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, If);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), json);
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await TSqlFormatter.Cli.SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(formatter.Format(If, options, new FormatRequest()).Text, output.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
