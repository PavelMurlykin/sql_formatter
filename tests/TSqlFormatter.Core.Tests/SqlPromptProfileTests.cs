using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlPromptProfileTests
{
    private static FormattingOptions Profile => new SqlFormatterConfigurationSerializer().Deserialize(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NativeProfiles", "ADIR_SQL_Main.json")));

    [Fact]
    public void Imported_profile_roundtrips_every_new_setting_through_the_editor_and_user_store()
    {
        var model = new SettingsEditorModel(Profile);
        var imported = new SettingsEditorModel();
        imported.Import(model.Export());
        Assert.Equal(model.Export(), imported.Export());
        var store = new UserProfileStore();
        store.Save("ADIR_SQL_Main", imported.Options);
        Assert.Equal(model.Export(), new SettingsEditorModel(UserProfileStore.Deserialize(store.Serialize()).Get("ADIR_SQL_Main")).Export());
        Assert.Equal(160, Profile.General.MaxLineWidth);
        Assert.Equal("upper", Profile.Rules.Get("textCase.globalVariable").Choice);
        Assert.Equal(100, Profile.Rules.Get("layout.dmlCompact").Threshold.Value);
        Assert.Equal(55, Profile.Rules.Get("layout.subqueryCompact").Threshold.Value);
        Assert.Equal(78, Profile.Rules.Get("layout.caseCompact").Threshold.Value);
    }

    [Theory]
    [InlineData("select @@rowcount, @MixedName;", "SELECT @@ROWCOUNT, @MixedName;")]
    [InlineData("print '  Value  '   ;", "PRINT '  Value  ';")]
    [InlineData("CREATE PROCEDURE dbo.GetTypes AS BEGIN SET NOCOUNT ON; SELECT type_code, type_name FROM dbo.acc_types; END;", "CREATE PROCEDURE dbo.GetTypes\nAS\nBEGIN\n    SET NOCOUNT ON;\n\n    SELECT type_code, type_name FROM dbo.acc_types;\nEND;")]
    [InlineData("SELECT TerritoryID, Name, [Group], SalesYTD AS YearToDate, SalesLastYear AS LastYear FROM Sales.SalesTerritory;", "SELECT\n    TerritoryID\n    , Name\n    , [Group]\n    , SalesYTD AS YearToDate\n    , SalesLastYear AS LastYear\nFROM Sales.SalesTerritory;")]
    public void Verified_profile_examples_have_expected_layout(string input, string expected)
    {
        var result = new ScriptDomSqlFormatter().Format(input, Profile, new FormatRequest());
        Assert.True(result.ParseSucceeded);
        Assert.Equal(expected, result.Text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
    }

    public static IEnumerable<object[]> Shapes() => new[]
    {
        "SELECT Id, Name FROM dbo.Items WHERE Id > 0 AND Name IS NOT NULL ORDER BY Id, Name;",
        "CREATE PROCEDURE dbo.Items @id int, @name nvarchar(50) = NULL AS BEGIN DECLARE @short int = 1, @longer nvarchar(50) = '  Value  '; SELECT Id, Name FROM dbo.Items WHERE Id = @id; END;",
        "SELECT CASE WHEN Id = 1 THEN 'First' ELSE 'Other' END AS Name FROM dbo.Items;",
        "SELECT CASE WHEN Id = 1 AND Amount > 0 THEN N'First item with an unusually long description' WHEN Id = 2 THEN N'Second' ELSE N'Other' END AS ItemName FROM dbo.Items;",
        "INSERT INTO dbo.Items (Id, Name) VALUES (1, 'First'), (2, 'Second');",
        "CREATE TABLE dbo.Items (Id int NOT NULL, LongName nvarchar(50) NULL, CONSTRAINT PK_Items PRIMARY KEY (Id));",
        "SELECT t.Id FROM dbo.Items t INNER JOIN dbo.Other s ON s.Id = t.Id AND s.Name IS NOT NULL WHERE t.Id > 0 AND t.Id BETWEEN 1 AND 10;",
        "UPDATE dbo.Items SET Name = 'Example', Amount = 200 WHERE Id = 1; DELETE FROM dbo.Items WHERE Id = 2;",
        "SELECT a.Id FROM (SELECT Id FROM dbo.Items WHERE Name IS NOT NULL AND Amount > 0) AS a;",
        "SELECT Id FROM dbo.Items WHERE Id IN (1, 2, 3, 4, 5);",
        "WITH items (Id, Name) AS (SELECT Id, Name FROM dbo.Items) SELECT Id, Name FROM items;",
        "DECLARE @small int = 1, @longVariableName decimal(10, 2) = 20, @text nvarchar(50);",
        "SELECT Id, -- first\nName -- second\nFROM dbo.Items;",
        "USE tempdb;\nGO\n\n\nSELECT 1;\n\n\nSELECT 2;",
        "RESTORE DATABASE MyDatabase FROM DISK = 'backup.bak' WITH MOVE 'Data' TO 'C:\\data.mdf', MOVE 'Log' TO 'C:\\log.ldf';"
        , "SELECT TRY_CAST(calc.name AS INT) FROM dbo.Items s CROSS APPLY (SELECT TRIM(s.name) AS name) calc WHERE calc.name <> '';"
        , "SELECT Id, ROW_NUMBER() OVER (PARTITION BY Name ORDER BY Id) AS rn FROM dbo.Items;"
        , "SELECT CASE WHEN Id = 1 THEN 'First extended description' -- keep\n WHEN Id = 2 THEN 'Second extended description' ELSE 'Other extended description' END FROM dbo.Items;"
        , "CREATE PROCEDURE dbo.GetItems @items MixedSchema.MyTableType READONLY AS BEGIN SELECT * FROM @items; END;"
        , "CREATE PROCEDURE dbo.P AS BEGIN IF @id = 1 BEGIN IF @x = 2 BEGIN EXEC dbo.Other @data = @packet; INSERT INTO dbo.Log (Code, Name, Description) VALUES (@id, @name, 'A longer description to prevent collapsing the statement'); END; END; END;"
        , "SELECT STUFF((SELECT ', ' + CONVERT(varchar(30), Id) FROM dbo.Items WHERE Name IS NOT NULL AND Amount > 0 FOR XML PATH(''), TYPE).value('.', 'varchar(max)'), 1, 2, '') AS Names;"
    }.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Profile_preserves_tokens_and_is_idempotent_for_nested_and_top_level_shapes(string source)
    {
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(source, Profile, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, Profile, new FormatRequest()).Text);
        var parser = new ScriptDomSqlParser();
        var after = parser.Parse(first.Text, SqlDialectVersion.Auto);
        Assert.True(after.ParseSucceeded);
        Assert.Equal(Tokens(parser.Parse(source, SqlDialectVersion.Auto)), Tokens(after));
    }

    [Fact]
    public void Formatting_directives_keep_the_original_region_and_do_not_match_string_contents()
    {
        const string region = "\n  select @Mixed =  1 +   2;\n  ";
        var source = "CREATE PROCEDURE dbo.P AS BEGIN\n-- SQL Prompt formatting off" + region
            + "-- SQL Prompt formatting on\nselect @@rowcount; END;";
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(source, Profile, new FormatRequest());
        Assert.Contains("-- SQL Prompt formatting off" + region + "-- SQL Prompt formatting on", first.Text);
        Assert.Contains("SELECT @@ROWCOUNT;", first.Text);
        Assert.Equal(first.Text, formatter.Format(first.Text, Profile, new FormatRequest()).Text);
        Assert.Contains("SELECT '-- SQL Prompt formatting off'", formatter.Format("select '-- SQL Prompt formatting off';", Profile, new FormatRequest()).Text);
        Assert.Contains("MixedSchema.MyTableType", formatter.Format(
            "CREATE PROCEDURE dbo.P @items MixedSchema.MyTableType READONLY AS SELECT 1;", Profile, new FormatRequest()).Text);
    }

    private static IEnumerable<string> Tokens(SqlParseResult p) => p.Tokens
        .Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile))
        .Select(t => t.TokenType is TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral
            or TSqlTokenType.QuotedIdentifier or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment
            ? t.Text : t.Text.ToUpperInvariant());
}
