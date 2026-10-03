using Newtonsoft.Json.Linq;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlCompleteWorkingProfileTests
{
    private static FormattingOptions Profile(string name) => new SqlFormatterConfigurationSerializer().Deserialize(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NativeProfiles", name + ".json")));

    [Theory]
    [InlineData("AV_Profile", "remove")]
    [InlineData("Right-aligned-EPM-AWB2", "insert")]
    public void Working_profiles_import_and_roundtrip_all_native_values(string name, string commaSpace)
    {
        var options = Profile(name);
        Assert.Equal(573, options.Rules.Overrides.Count);
        Assert.Equal(commaSpace, options.Rules.Get("stackedList.spaceAfterLeadingComma").Choice);
        Assert.Equal("upper", options.Rules.Get("textCase.keyword").Choice);
        Assert.Equal("preserve", options.Rules.Get("textCase.identifier").Choice);
        Assert.Equal("relativeSpaces", options.Rules.Get("select.from.keywordIndent").Indent.Style);
        var model = new SettingsEditorModel(options);
        var imported = new SettingsEditorModel();
        imported.Import(model.Export());
        Assert.Equal(model.Export(), imported.Export());
        var store = new UserProfileStore();
        store.Save(name, imported.Options);
        Assert.Equal(model.Export(), new SettingsEditorModel(UserProfileStore.Deserialize(store.Serialize()).Get(name)).Export());
    }

    [Theory]
    [InlineData("relative", 2, 4, 8)]
    [InlineData("absolute", 2, 4, 8)]
    [InlineData("relativeSpaces", 2, 4, 2)]
    [InlineData("absoluteSpaces", 2, 4, 2)]
    [InlineData("relativeSpaces", -1, 8, -1)]
    public void Space_offsets_do_not_multiply_by_the_global_indent_size(string style, int offset, int size, int width)
    {
        Assert.Equal(width, new IndentRule(true, offset, style: style).Width(size));
    }

    [Theory]
    [InlineData("relativeSpaces", 2, 6)]
    [InlineData("relativeSpaces", -1, 3)]
    [InlineData("absoluteSpaces", 2, 2)]
    public void Space_offsets_apply_to_real_from_keyword_boundaries(string style, int offset, int column)
    {
        var rules = FormattingOptions.Default.Rules.With("select.from.breakBefore", RuleValue.FromChoice("always"))
            .With("select.from.keywordIndent", RuleValue.FromIndent(new IndentRule(true, offset, style: style)));
        var options = FormattingOptions.Default.With(indent: new IndentOptions(8), rules: rules);
        var text = Verify("    SELECT Id FROM dbo.Items;", options);
        Assert.Contains("\n" + new string(' ', column) + "FROM dbo.Items", text);
        Assert.Equal(style, new SettingsEditorModel(new SqlFormatterConfigurationSerializer().Deserialize(
            new SqlFormatterConfigurationSerializer().SerializeV2(options))).Options.Rules.Get("select.from.keywordIndent").Indent.Style);
    }

    public static IEnumerable<object[]> Cases()
    {
        var sources = new[]
        {
            "SELECT Id, Name FROM dbo.Items WHERE Amount > 0 AND Status = 'Active' GROUP BY Id, Name ORDER BY Id;",
            "CREATE PROCEDURE dbo.ReadItems @id INT, @name VARCHAR(50) AS BEGIN SELECT Id, Name FROM dbo.Items WHERE Id = @id; END;",
            "CREATE TABLE dbo.Items (Id INT NOT NULL, Name VARCHAR(50) NULL, CONSTRAINT PK_Items PRIMARY KEY (Id));",
            "DECLARE @a INT = 1, @longName VARCHAR(20) = 'Text'; SELECT @a;",
            "INSERT INTO dbo.Items (Id, Name) VALUES (1, 'First'),\n    (2, 'Second'),\n    (3, 'Third');",
            "SELECT CASE WHEN Id = 1 AND Name IS NOT NULL THEN 'One' ELSE 'Other' END AS Description FROM dbo.Items;",
            "WITH items AS (SELECT Id, Name FROM dbo.Items) SELECT Id, Name FROM items;",
            "CREATE VIEW dbo.ItemNames AS SELECT Id, Name FROM dbo.Items;",
            "CREATE TABLE [MiXeD] ([Id] [int] NOT NULL, [Name] [nvarchar](MAX) NULL);",
            "UPDATE dbo.Items SET Status = CASE WHEN Id > 1 THEN 1 WHEN Id = 1 THEN 2 ELSE 3 END;",
            "SELECT SUM(CASE WHEN a = 1 THEN 1 ELSE 0 END), CASE WHEN b = 2 THEN 2 ELSE 3 END FROM dbo.Items;",
            "WITH c AS (SELECT a, b FROM dbo.Items) SELECT (SELECT MAX(a) FROM c) AS Value FROM c;"
        };
        foreach (var name in new[] { "AV_Profile", "Right-aligned-EPM-AWB2" })
            foreach (var source in sources) yield return new object[] { name, source };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Working_profiles_preserve_sql_tokens_and_repeat_stably(string name, string source) => Verify(source, Profile(name));

    [Theory]
    [InlineData("AV_Profile", 3, 972, 92)]
    [InlineData("Right-aligned-EPM-AWB2", 4, 977, 97)]
    public void Mapping_accounts_for_every_original_field_and_records_uncertain_modes(
        string name, int column, int count, int uncertain)
    {
        var originals = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "profile-values.tsv"))
            .Skip(1).Select(line => line.Split('\t')).Where(row => row[column] != "<missing>").ToDictionary(row => row[0]);
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "NativeProfiles", "sql-complete-working-mapping.tsv"))
            .Skip(1).Select(line => line.Split('\t')).Where(row => row[0] == name).ToArray();
        Assert.Equal(count, rows.Length);
        Assert.Equal(count, rows.Select(row => row[1]).Distinct().Count());
        Assert.Equal(uncertain, rows.Count(row => row[5] == "native_policy_numeric_mode_not_certified"));
        Assert.Equal(8, rows.Count(row => row[5] == "previous_user_approved_exclusion"));
        var options = Profile(name);
        var rules = JObject.Parse(new SqlFormatterConfigurationSerializer().SerializeV2(options))["rules"]!;
        foreach (var row in rows)
        {
            Assert.Equal(originals[row[1]][column], row[2]);
            if (row[5] == "previous_user_approved_exclusion") continue;
            var root = options.Rules.Overrides.Keys.OrderByDescending(key => key.Length)
                .First(key => row[3] == key || row[3].StartsWith(key + ".", StringComparison.Ordinal));
            Assert.True(JToken.DeepEquals(rules[root], JToken.Parse(row[4])), row[1]);
        }
    }

    [Theory]
    [InlineData("AV_Profile", "Id,\nName")]
    [InlineData("Right-aligned-EPM-AWB2", "Id, Name")]
    public void Column_policies_apply_in_document_stored_code_and_nested_queries(string name, string columns)
    {
        var profile = Profile(name);
        var options = profile.With(rules: profile.Rules.With("select.singleLine.maxCharacters",
            RuleValue.FromThreshold(new ThresholdRule(false, 50))));
        foreach (var source in new[] { "SELECT Id, Name FROM dbo.Items;",
            "CREATE PROCEDURE dbo.ReadItems AS BEGIN SELECT Id, Name FROM dbo.Items WHERE Id = 1 AND Name IS NOT NULL; END;",
            "SELECT (SELECT Id, Name FOR XML PATH('')) AS Value;" })
        {
            var formatted = Verify(source, options);
            Assert.Contains("SELECT\n" + columns, formatted);
        }
    }
    [Fact]
    public void Epm_short_query_threshold_overrides_column_wrapping()
    {
        const string source = "SELECT Id, Name FROM dbo.Items;";
        Assert.Contains("SELECT\nId,\nName", Verify(source, Profile("AV_Profile")));
        Assert.Equal(source, Verify(source, Profile("Right-aligned-EPM-AWB2")));
    }
    [Theory]
    [InlineData("AV_Profile")]
    [InlineData("Right-aligned-EPM-AWB2")]
    public void Long_union_chains_share_the_first_branch_anchor(string name)
    {
        var branches = Enumerable.Range(1, 26).Select(value => "    SELECT\n        'Item" + value + "'");
        var source = "CREATE PROCEDURE dbo.LoadItems AS BEGIN INSERT INTO dbo.Items (Name)\n"
            + string.Join("\n    UNION\n", branches) + "; END;";
        var formatted = Verify(source, Profile(name));
        Assert.Equal(25, System.Text.RegularExpressions.Regex.Matches(formatted, @"\bUNION\b").Count);
    }

    [Fact]
    public void Epm_short_query_policy_also_applies_inside_modules_and_scalar_subqueries()
    {
        var profile = Profile("Right-aligned-EPM-AWB2");
        Assert.Contains("SELECT Id, Name FROM dbo.Items", Verify(
            "CREATE PROCEDURE dbo.ReadItems AS BEGIN SELECT Id, Name FROM dbo.Items; END;", profile));
        Assert.Contains("SELECT Id, Name FOR XML PATH('')", Verify(
            "SELECT (SELECT Id, Name FOR XML PATH('')) AS Value;", profile));
    }
    [Fact]
    public void Token_validation_allows_selected_builtin_type_casing_and_rejects_other_changes()
    {
        var options = Profile("AV_Profile");
        var parser = new ScriptDomSqlParser();
        const string source = "CREATE TABLE [MiXeD] ([Id] [int] NOT NULL, [Name] [nvarchar](50) DEFAULT 'Original'); -- Original";
        var before = parser.Parse(source, SqlDialectVersion.Auto);
        var formatted = new ScriptDomSqlFormatter().Format(source, options, new FormatRequest()).Text;
        Assert.Contains("[INT]", formatted);
        Assert.Contains("[MiXeD]", formatted);
        Assert.True(SqlTokenSafety.PreservesTokens(before, parser.Parse(formatted, SqlDialectVersion.Auto), options));
        foreach (var changed in new[] { formatted.Replace("[MiXeD]", "[MIXED]"), formatted.Replace("'Original'", "'Changed'"),
            formatted.Replace("-- Original", "-- Changed"), formatted.Replace("[Id]", "[OtherId]") })
            Assert.False(SqlTokenSafety.PreservesTokens(before, parser.Parse(changed, SqlDialectVersion.Auto), options));
    }
    private static string Verify(string source, FormattingOptions options)
    {
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(source, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error || d.Code is "TSF3006" or "TSF3007");
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        var parser = new ScriptDomSqlParser();
        Assert.True(SqlTokenSafety.PreservesTokens(parser.Parse(source, SqlDialectVersion.Auto), parser.Parse(first.Text, SqlDialectVersion.Auto), options));
        return first.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
