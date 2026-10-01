using System.Reflection;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class FinalParityAuditTests
{
    [Theory]
    [InlineData("ReadableVertical")]
    [InlineData("CompactQueries")]
    public async Task Ready_presets_use_identical_core_preview_and_cli_output(string id)
    {
        var options = NativeFormattingPresets.Profiles.Single(p => p.Id == id).Options;
        const string sql = "EXEC dbo.p @a = 1, @b = N'KeepCase';\nSELECT a, b FROM dbo.T WHERE a = 1 AND b = 2;\nCREATE TABLE dbo.NewTable (Id int NOT NULL, Name nvarchar(50), CONSTRAINT PK_NewTable PRIMARY KEY (Id));\n";
        string expected = new ScriptDomSqlFormatter().Format(sql, options, new FormatRequest()).Text;
        Assert.Equal(expected, SettingsPreview.Format(sql, options).Result!.Text);
        string directory = Path.Combine(Path.GetTempPath(), "tsql-parity-adapters-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, sql);
            File.WriteAllText(Path.Combine(directory, ".tsqlformatter.json"), new SettingsEditorModel(options).Export());
            using var input = new StringReader("");
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await TSqlFormatter.Cli.SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(expected, output.ToString());
            Assert.Empty(error.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Every_original_path_resolves_to_a_real_typed_editable_setting_and_an_executable_test()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity");
        var ledger = File.ReadAllLines(Path.Combine(directory, "coverage.tsv")).Skip(1).Select(l => l.Split('\t')).ToArray();
        Assert.Equal(977, ledger.Length);
        Assert.Equal(577, ledger.Select(r => r[0].Split('.')[0]).Distinct().Count());
        Assert.Equal(969, ledger.Count(r => r[2] == "covered"));
        Assert.Equal(8, ledger.Count(r => r[2] == "not_applicable"));
        var editor = new SettingsEditorModel();
        var types = typeof(FinalParityAuditTests).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.Namespace == typeof(FinalParityAuditTests).Namespace).ToDictionary(t => t.Name);
        foreach (var row in ledger)
        {
            Assert.Contains(row[2], new[] { "covered", "not_applicable" });
            Assert.False(string.IsNullOrWhiteSpace(row[5]));
            string[] evidence = row[4].Split('.');
            Assert.Equal(2, evidence.Length);
            Assert.True(types.TryGetValue(evidence[0], out var type), row[0] + ": missing evidence class " + row[4]);
            var method = type!.GetMethod(evidence[1], BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.Contains(method!.GetCustomAttributes(), a => a is FactAttribute);
            if (row[2] == "not_applicable")
            {
                Assert.StartsWith("Subquery_OptionHints_", row[0]);
                Assert.Contains("User-approved 2026-09-30", row[5]);
                Assert.Equal("-", row[3]);
                continue;
            }
            string[] mapping = row[3].Split(':');
            string key = mapping[0];
            var root = RuleCatalog.Default.Definitions.Keys.OrderByDescending(k => k.Length)
                .FirstOrDefault(k => key == k || key.StartsWith(k + ".", StringComparison.Ordinal));
            var fields = root is null ? editor.Fields.Where(f => f.Id == key).ToArray()
                : editor.Fields.Where(f => f.RuleKey == root && (key == root || f.Id == "rules." + key)).ToArray();
            Assert.NotEmpty(fields);
            if (mapping.Length > 1)
            {
                Assert.Equal(3, mapping.Length);
                Assert.Single(fields);
                Assert.Equal(mapping[1], fields[0].Kind.ToString().ToLowerInvariant());
            }
            foreach (var field in fields)
            {
                Assert.NotNull(editor.Get(field.Id));
                Assert.Contains(field, editor.Find(field.Id));
            }
        }
        // The correspondence generator additionally verifies the immutable snapshot SHA-256 and exact path set.
        Assert.NotEmpty(ProfileCorrespondenceReport.Create(File.ReadAllBytes(Path.Combine(directory, "profile-values.tsv")),
            File.ReadAllText(Path.Combine(directory, "coverage.tsv"))));
    }
}
