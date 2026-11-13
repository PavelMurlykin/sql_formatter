using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;
using TSqlFormatter.Core.Analysis;
using TSqlFormatter.Core.History;
using TSqlFormatter.Core.QueryResults;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.ReleaseSmoke;

internal static class Program
{
    private static readonly string[] Profiles = { "ADIR_SQL_Main", "AV_Profile", "Right-aligned-EPM-AWB2" };
    private static readonly string[] Sources =
    {
        "select Id, Name from dbo.Items where Amount > 0 and Status = N'KeepCase' order by Id, Name;",
        "CREATE PROCEDURE dbo.ReadItems @id INT, @name VARCHAR(50) AS BEGIN SELECT Id, Name FROM dbo.Items WHERE Id = @id; END;",
        "CREATE TABLE dbo.Items (Id INT NOT NULL, LongName VARCHAR(50) NULL, CONSTRAINT PK_Items PRIMARY KEY (Id));",
        "DECLARE @a INT = 1, @longName VARCHAR(20) = 'Text'; SELECT @a;",
        "INSERT INTO dbo.Items (Id, Name) VALUES (1, 'First'), (2, 'Second');",
        "SELECT CASE WHEN Id = 1 AND Name IS NOT NULL THEN 'One' ELSE 'Other' END AS Description FROM dbo.Items;",
        "WITH items AS (SELECT Id, Name FROM dbo.Items) SELECT Id, Name FROM items;",
        "CREATE VIEW dbo.ItemNames AS SELECT Id, Name FROM dbo.Items;",
        "SELECT a.Id FROM dbo.Items a INNER JOIN dbo.Other b ON b.Id = a.Id WHERE a.Id BETWEEN 1 AND 10;",
        "UPDATE dbo.Items SET Name = 'Example', Amount = 200 WHERE Id = 1; DELETE FROM dbo.Items WHERE Id = 2;",
        "SELECT a.Id FROM (SELECT Id FROM dbo.Items WHERE Name IS NOT NULL AND Amount > 0) AS a;",
        "SELECT Id FROM dbo.Items WHERE Id IN (1, 2, 3, 4, 5);",
        "SELECT Id, -- first\nName -- second\nFROM dbo.Items;",
        "USE tempdb;\nGO\nSELECT 1;\nSELECT 2;",
        "SELECT Id, ROW_NUMBER() OVER (PARTITION BY Name ORDER BY Id) AS rn FROM dbo.Items;",
        "USE [WorkDb]\nGO\nSELECT [Id] FROM [WorkDb].[dbo].[Items]\nEXEC [dbo].[ReadItems]",
        "IF @var=1 BEGIN SELECT 1 END\n\tELSE IF @var=2 BEGIN SELECT 2 END\n\t\tELSE IF @var=3 BEGIN SELECT 3 END",
        "SELECT [Group], [Order Details], [a]]b], N'[dbo].[Items];' FROM [dbo].[Items] -- keep [brackets];",
        "CREATE FUNCTION [dbo].[ReadItems]() RETURNS TABLE AS RETURN (SELECT [Id] FROM [dbo].[Items])"
    };

    private static int Main(string[] args)
    {
        if (args.Length > 2 && File.Exists(args[2]))
        {
            var hostAssembly = System.Reflection.Assembly.LoadFrom(args[2]);
            Console.WriteLine("Preloaded host ScriptDom: " + hostAssembly.GetName().Version);
        }
        return Verify(args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Verify(string[] args)
    {
        try
        {
            var directory = Path.GetFullPath(args[0]);
            var output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            Require(Path.GetDirectoryName(typeof(ScriptDomSqlFormatter).Assembly.Location) == AppContext.BaseDirectory.TrimEnd('\\'),
                "Formatter must load from the extracted installer.");
            Require(Path.GetDirectoryName(typeof(EditorFeaturesSettings).Assembly.Location) == AppContext.BaseDirectory.TrimEnd('\\'),
                "Configuration must load from the extracted installer.");
            VerifyEditorFeatures(output);
            VerifySafetyFeatures();
            VerifyQueryResultFeatures();
            var formatter = new ScriptDomSqlFormatter();
            var parser = new ScriptDomSqlParser();
            foreach (var name in Profiles)
            {
                var imported = new SqlFormatterProfileExchange().Import(Path.Combine(directory, name + ".json"));
                Require(imported.Succeeded, name + ": import failed");
                var options = imported.Options!;
                var model = new SettingsEditorModel(options);
                var roundtrip = new SettingsEditorModel();
                roundtrip.Import(model.Export());
                Require(model.Export() == roundtrip.Export(), name + ": settings round trip failed");
                var store = new UserProfileStore();
                store.Save(name, options);
                store.SetDefault("user:" + name);
                var restored = UserProfileStore.Deserialize(store.Serialize()).ResolveDefault(FormattingOptions.Default);
                Require(model.Export() == new SettingsEditorModel(restored).Export(), name + ": default profile persistence failed");
                VerifyQueryResultProfile(name, restored, output);
                if (name != "ADIR_SQL_Main") Require(options.Rules.Overrides.Count == 573, name + ": rules missing");
                else Require(options.Rules.Get("syntax.removeOptionalBrackets").Boolean
                    && options.Rules.Get("syntax.addSemicolons").Boolean
                    && options.Rules.Get("code.if.breakAfterElseIf").Choice == "never", name + ": syntax/ELSE IF policies missing");
                for (int index = 0; index < Sources.Length; index++)
                {
                    string source = Sources[index];
                    var result = formatter.Format(source, restored, new FormatRequest());
                    Require(result.ParseSucceeded && !result.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error), name + ": format failed " + index);
                    Require(!result.Diagnostics.Any(d => d.Code is "TSF3006" or "TSF3007"), name + ": unsafe or unstable output " + index);
                    Require(result.Text == formatter.Format(result.Text, restored, new FormatRequest()).Text, name + ": unstable output " + index);
                    var before = parser.Parse(source, SqlDialectVersion.Auto);
                    var after = parser.Parse(result.Text, SqlDialectVersion.Auto);
                    Require(after.ParseSucceeded && SqlTokenSafety.PreservesTokens(before, after, restored), name + ": unexpected token edits " + index);
                    File.WriteAllText(Path.Combine(output, name + "-" + index + ".sql"), result.Text, new UTF8Encoding(false));
                }
                Console.WriteLine(name + ": import, settings/store round trip, " + Sources.Length + " SQL cases, token safety and idempotence passed.");
            }
            using var sha = SHA256.Create();
            foreach (var assembly in new[] { typeof(ScriptDomSqlFormatter).Assembly, typeof(SettingsEditorModel).Assembly, typeof(TSqlParser).Assembly })
                Console.WriteLine(assembly.GetName().Name + " " + assembly.GetName().Version + " sha256=" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assembly.Location))).Replace("-", ""));
            Console.WriteLine("Process architecture: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void VerifyEditorFeatures(string output)
    {
        var settings = new EditorFeaturesSettings(new AnalysisConfiguration(new[] { "TSQL001" },
            new Dictionary<string, AnalysisSeverity> { ["TSQL004"] = AnalysisSeverity.Info }, false),
            historyMaxSnapshots: 2, historyMaxCharacters: 1000, historyMaxAgeDays: 7);
        var restored = EditorFeaturesSettings.Deserialize(settings.Serialize());
        Require(!restored.Analysis.Enabled && restored.Analysis.DisabledRules.Contains("TSQL001") &&
            restored.Analysis.SeverityOverrides["TSQL004"] == AnalysisSeverity.Info &&
            restored.HistoryMaxSnapshots == 2 && restored.HistoryMaxCharacters == 1000 && restored.HistoryMaxAgeDays == 7,
            "Packaged analysis/history settings round trip failed.");
        var project = new AnalysisConfigurationParser().Parse(
            "{\"analysis\":{\"enabled\":true,\"disabled\":[],\"severity\":{\"TSQL001\":\"error\"}}}", restored.Analysis);
        var parsed = new ScriptDomSqlParser().Parse("SELECT * FROM dbo.ReleaseSmoke;", SqlDialectVersion.Auto);
        var diagnostics = new SqlAnalyzer().Analyze(parsed, project.Configuration!.ToAnalysisOptions()).Diagnostics;
        Require(project.Succeeded && diagnostics.Count == 1 && diagnostics[0].Code == "TSQL001" &&
            diagnostics[0].Severity == AnalysisSeverity.Error, "Packaged project analysis override failed.");
        Require(new SqlAnalyzer().Analyze(parsed, restored.Analysis.ToAnalysisOptions()).Diagnostics.Count == 0,
            "Packaged disabled analysis still produced findings.");
        var session = new EditorHistorySession();
        session.Remember(1, "untitled.sql", "SELECT 1; -- discarded", true, true, 1000, "test-server", "test-db");
        var snapshot = session.Take(1, "close", true)!;
        Require(snapshot.Content.Contains("discarded") && session.Take(1, "close") == null,
            "Packaged close-time history lost the unsaved buffer.");
        var store = restored.CreateHistoryStore(Path.Combine(output, "history"));
        var now = DateTime.UtcNow;
        Require(store.Save("expired.sql", "old", now.AddDays(-8)) != null, "Packaged history write failed.");
        Require(store.Save(snapshot.FilePath, snapshot.Content, now, snapshot.Server, snapshot.Database, snapshot.Reason) != null,
            "Packaged close snapshot write failed.");
        var item = store.List().Single();
        Require(item.Content == snapshot.Content && item.Server == "test-server" && item.Database == "test-db" && item.Reason == "close",
            "Packaged age retention or snapshot metadata round trip failed.");
        store.Save("second.sql", "second", now.AddSeconds(1));
        store.Save("third.sql", "third", now.AddSeconds(2));
        Require(store.List().Count == 2 && store.List().All(s => s.FilePath != snapshot.FilePath),
            "Packaged count retention failed.");
        store.Enabled = false; store.MaxAgeDays = 1; store.Prune(now.AddDays(20));
        Require(store.List().Count == 2 && store.Save("off.sql", "off") == null,
            "Packaged disabled history modified its snapshots.");
        Console.WriteLine("Editor features: analysis settings/project overrides, unsaved close buffer, metadata, age/count retention and disabled history passed.");
    }

    private static void VerifySafetyFeatures()
    {
        var settings = new EditorFeaturesSettings(analysis: new AnalysisConfiguration(
            new[] { "TSQL004", "TSQL005" }, new Dictionary<string, AnalysisSeverity>(), false),
            environments: new SqlEnvironmentConfiguration(new[] {
                new SqlEnvironmentRule(SqlEnvironmentKind.Dev, "release-server"),
                new SqlEnvironmentRule(SqlEnvironmentKind.Prod, "release-server", "Accounting", color: "#902020"),
                new SqlEnvironmentRule(SqlEnvironmentKind.Test, documentContains: @"\offline\") }));
        var restored = EditorFeaturesSettings.Deserialize(settings.Serialize());
        var match = restored.Environments.Match(new(@"C:\queries\smoke.sql", "RELEASE-SERVER", "accounting"));
        Require(match.Kind == SqlEnvironmentKind.Prod && match.Color == "#902020",
            "Packaged environment round trip or PROD precedence failed.");
        Require(restored.Environments.Match(new("smoke.sql", "release-server", "other")).Kind == SqlEnvironmentKind.Dev,
            "Packaged environment fields must be combined with AND.");
        Require(restored.Environments.Match(new(@"C:\release-server\smoke.sql")).Kind == SqlEnvironmentKind.Unknown,
            "Packaged environment must not infer a server from a file path.");
        Require(restored.Environments.Match(new(@"C:\offline\smoke.sql")).Kind == SqlEnvironmentKind.Test,
            "Packaged explicit offline document rule failed.");
        Require(EditorFeaturesSettings.Deserialize("{\"version\":1,\"analysis\":{},\"history\":{}}")
            .Environments.Rules.Count == 0, "Packaged legacy settings must migrate to no environment rules.");

        const string source = "-- release smoke\r\nDROP TABLE dbo.T;\r\nTRUNCATE TABLE dbo.T;\r\nUPDATE dbo.T SET A=1;\r\nDELETE FROM dbo.T;";
        var parser = new ScriptDomSqlParser();
        var parsed = parser.Parse(source, SqlDialectVersion.Auto);
        Require(parsed.ParseSucceeded, "Packaged safety input failed to parse.");
        Require(new SqlAnalyzer().Analyze(parsed, restored.Analysis.ToAnalysisOptions()).Diagnostics.Count == 0,
            "Packaged normal analysis should remain disabled.");
        var analyzer = new DangerousSqlAnalyzer();
        var findings = analyzer.Analyze(parsed);
        Require(findings.Select(d => d.Code).SequenceEqual(new[] { "TSQL006", "TSQL007", "TSQL004", "TSQL005" }) &&
            findings.Select(d => d.Line).SequenceEqual(new[] { 2, 3, 4, 5 }) &&
            findings.All(d => d.Severity == AnalysisSeverity.Error) &&
            findings[0].Span.StartOffset == source.IndexOf("DROP", StringComparison.Ordinal),
            "Packaged dangerous-command detection, ordering or original positions failed.");
        var safe = parser.Parse("SELECT N'DROP TABLE dbo.T;'; EXEC(N'DELETE FROM dbo.T;'); " +
            "UPDATE dbo.T SET A=1 WHERE Id=1; DELETE FROM dbo.T WHERE Id=1;", SqlDialectVersion.Auto);
        Require(safe.ParseSucceeded && analyzer.Analyze(safe).Count == 0,
            "Packaged safety check treated dynamic strings or filtered writes as dangerous statements.");
        var invalid = parser.Parse("DROP TABLE ;", SqlDialectVersion.Auto);
        Require(!invalid.ParseSucceeded, "Packaged invalid SQL unexpectedly parsed.");
        bool rejected = false;
        try { analyzer.Analyze(invalid); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Packaged safety analyzer must reject incomplete parses.");
        Console.WriteLine("Stage 10: environment persistence/AND/PROD precedence/UNKNOWN/offline rules, legacy migration, independent dangerous-command checks, positions, false positives and parse rejection passed.");
    }

    private static void VerifyQueryResultFeatures()
    {
        var quoted = TsvTable.Parse("\"a\tb\"\t\"line1\r\nline2\"\t\"\"\r\n");
        Require(quoted.Rows.Count == 1 && quoted.ColumnCount == 3 && quoted.Rows[0][0].Text == "a\tb" &&
            quoted.Rows[0][1].Text == "line1\r\nline2" && quoted.Rows[0][2].Text == "", "Packaged TSV decoding failed.");
        var statistics = TsvStatistics.Calculate(TsvTable.Parse("1\t2.5\r\nNULL\ttext"));
        Require(statistics.TotalCells == 4 && statistics.NumericCount == 2 && statistics.SkippedCount == 2 &&
            statistics.Sum == 3.5m && statistics.Average == 1.75m, "Packaged numeric statistics failed.");
        var empty = TsvStatistics.Calculate(TsvTable.Parse("NULL\ttext"));
        Require(empty.NumericCount == 0 && empty.Sum == 0 && empty.Average == null, "Packaged empty statistics failed.");
        var exact = TsvStatistics.Calculate(TsvTable.Parse("79228162514264337593543950335\n1\n-1"));
        Require(exact.Sum == decimal.MaxValue, "Packaged exact sum lost precision during accumulation.");
        RequireResultError(ResultDataError.NumericOverflow, () => TsvStatistics.Calculate(
            TsvTable.Parse("4000000000000000000000000000.1\n4000000000000000000000000000.1")));
        RequireResultError(ResultDataError.UnequalColumns, () => TsvTable.Parse("a\tb\nc"));
        RequireResultError(ResultDataError.TooLarge, () => TsvTable.Parse(new string('x', TsvTable.MaxCharacters + 1)));
        RequireResultError(ResultDataError.InvalidTable, () => QueryResultSql.CreateInsert(TsvTable.Parse("1"),
            "dbo.T; DROP TABLE T;", new(), FormattingOptions.Default));
        RequireResultError(ResultDataError.InRequiresOneColumn, () => QueryResultSql.CreateIn(TsvTable.Parse("1\t2"), new(), FormattingOptions.Default));
        string batched = QueryResultSql.CreateInsert(TsvTable.Parse(string.Join("\n", Enumerable.Range(1, 1001))),
            "dbo.ReleaseSmoke", new(), FormattingOptions.Default);
        var statements = QueryFragments<InsertStatement>(batched);
        Require(statements.Count == 2 && statements.Select(s => ((ValuesInsertSource)s.InsertSpecification.InsertSource).RowValues.Count)
            .SequenceEqual(new[] { 1000, 1 }), "Packaged INSERT batching failed.");
        Console.WriteLine("Stage 11: quoted TSV, limits/rejections, statistics, exact accumulation/overflow and 1001-row batching passed.");
    }

    private static void VerifyQueryResultProfile(string name, FormattingOptions options, string output)
    {
        var headers = new QueryResultOptions(firstRowHeaders: true);
        string numericIn = QueryResultSql.CreateIn(TsvTable.Parse("Id\r\n1\r\n2\r\nNULL"), headers, options);
        string numericSql = "SELECT 1 WHERE x " + numericIn + ";";
        var numbers = QueryFragments<InPredicate>(numericSql).Single().Values;
        Require(numbers.Count == 3 && numbers[0] is IntegerLiteral && numbers[2] is NullLiteral,
            name + ": packaged numeric IN lost values or NULL.");
        string textIn = QueryResultSql.CreateIn(TsvTable.Parse("Value\n001\nО'Брайен\nNULL\n\"NULL\""), headers, options);
        string textSql = "SELECT 1 WHERE x " + textIn + ";";
        var texts = QueryFragments<InPredicate>(textSql).Single().Values;
        Require(texts.Count == 4 && texts[0] is StringLiteral leading && leading.Value == "001" && leading.IsNational &&
            texts[1] is StringLiteral escaped && escaped.Value == "О'Брайен" && texts[2] is NullLiteral &&
            texts[3] is StringLiteral literalNull && literalNull.Value == "NULL", name + ": packaged text IN lost data.");
        string insert = QueryResultSql.CreateInsert(TsvTable.Parse("Id\tName\tStamp\n1\tO'Brien\t2026-11-13\n2\tNULL\t2026-11-14"),
            "[dbo].[ReleaseSmoke]", headers, options);
        var spec = QueryFragments<InsertStatement>(insert).Single().InsertSpecification;
        var rows = ((ValuesInsertSource)spec.InsertSource).RowValues;
        Require(spec.Columns.Count == 3 && rows.Count == 2 && rows[0].ColumnValues[1] is StringLiteral value &&
            value.Value == "O'Brien" && rows[0].ColumnValues[2] is ConvertCall && rows[1].ColumnValues[1] is NullLiteral,
            name + ": packaged INSERT lost headers, strings, ISO dates or NULL.");
        string directory = Path.Combine(output, "query-results"); Directory.CreateDirectory(directory);
        foreach (var item in new[] { (Suffix: "IN-numbers", Sql: numericSql), (Suffix: "IN-text", Sql: textSql), (Suffix: "INSERT", Sql: insert) })
            File.WriteAllText(Path.Combine(directory, name + "-" + item.Suffix + ".sql"), item.Sql, new UTF8Encoding(false));
        Console.WriteLine(name + ": stage 11 numeric/text IN and INSERT with profile options passed.");
    }

    private static void RequireResultError(ResultDataError code, Action action)
    {
        try { action(); }
        catch (ResultDataException ex) { Require(ex.Code == code, "Unexpected packaged TSV error: " + ex.Code); return; }
        throw new InvalidOperationException("Packaged TSV operation did not reject " + code);
    }
    private static IReadOnlyList<T> QueryFragments<T>(string sql) where T : TSqlFragment
    {
        var parsed = new ScriptDomSqlParser().Parse(sql, SqlDialectVersion.Auto);
        Require(parsed.ParseSucceeded, "Packaged query-result SQL failed to parse.");
        var visitor = new QueryVisitor<T>(); parsed.Root!.Accept(visitor); return visitor.Items;
    }
    private sealed class QueryVisitor<T> : TSqlFragmentVisitor where T : TSqlFragment
    {
        internal List<T> Items { get; } = new();
        public override void Visit(TSqlFragment node) { if (node is T item) Items.Add(item); }
    }
}
