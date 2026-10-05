using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;
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
        "SELECT Id, ROW_NUMBER() OVER (PARTITION BY Name ORDER BY Id) AS rn FROM dbo.Items;"
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
                if (name != "ADIR_SQL_Main") Require(options.Rules.Overrides.Count == 573, name + ": rules missing");
                for (int index = 0; index < Sources.Length; index++)
                {
                    string source = Sources[index];
                    var result = formatter.Format(source, restored, new FormatRequest());
                    Require(result.ParseSucceeded && !result.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error), name + ": format failed " + index);
                    Require(result.Text == formatter.Format(result.Text, restored, new FormatRequest()).Text, name + ": unstable output " + index);
                    var before = parser.Parse(source, SqlDialectVersion.Auto);
                    var after = parser.Parse(result.Text, SqlDialectVersion.Auto);
                    Require(after.ParseSucceeded && TokenSignature(before) == TokenSignature(after), name + ": tokens changed " + index);
                    File.WriteAllText(Path.Combine(output, name + "-" + index + ".sql"), result.Text, new UTF8Encoding(false));
                }
                Console.WriteLine(name + ": import, settings/store round trip, 15 SQL cases, token safety and idempotence passed.");
            }
            using var sha = SHA256.Create();
            foreach (var assembly in new[] { typeof(ScriptDomSqlFormatter).Assembly, typeof(SettingsEditorModel).Assembly, typeof(TSqlParser).Assembly })
                Console.WriteLine(assembly.GetName().Name + " " + assembly.GetName().Version + " sha256=" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assembly.Location))).Replace("-", ""));
            Console.WriteLine("Process architecture: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static string TokenSignature(SqlParseResult result) => string.Join("\n", result.Tokens
        .Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile))
        .Select(t => t.TokenType is TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral
            or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment ? t.Text : t.Text.ToUpperInvariant().Trim('[', ']')));

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
