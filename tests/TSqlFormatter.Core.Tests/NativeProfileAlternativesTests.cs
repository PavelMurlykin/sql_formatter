using Newtonsoft.Json.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class NativeProfileAlternativesTests
{
    private static string ParityPath(string file) => Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", file);
    private static string ProfilePath(string file) => Path.Combine(AppContext.BaseDirectory, "NativeProfiles", file);

    [Fact]
    public void Both_ready_profiles_are_reproducible_native_v2_snapshots_and_lossless_in_the_editor()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        foreach (var profile in NativeFormattingPresets.Profiles)
        {
            var json = File.ReadAllText(ProfilePath(profile.Id + ".json"));
            Assert.Equal(serializer.SerializeV2(profile.Options), json.Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Equal(2, JObject.Parse(json)["version"]);
            var editor = new SettingsEditorModel(serializer.Deserialize(json));
            Assert.Equal(serializer.SerializeV2(profile.Options), editor.Export());
            var store = new UserProfileStore();
            store.Save(profile.Name, editor.Options);
            Assert.Equal(editor.Export(), new SettingsEditorModel(UserProfileStore.Deserialize(store.Serialize()).Get(profile.Name)).Export());
        }
        Assert.NotEqual(serializer.SerializeV2(NativeFormattingPresets.Profiles[0].Options), serializer.SerializeV2(NativeFormattingPresets.Profiles[1].Options));
    }

    [Fact]
    public void Correspondence_preserves_every_scalar_of_both_profiles_and_does_not_claim_xml_conversion()
    {
        var snapshot = File.ReadAllBytes(ParityPath("profile-values.tsv"));
        var coverage = File.ReadAllText(ParityPath("coverage.tsv"));
        var report = ProfileCorrespondenceReport.Create(snapshot, coverage);
        Assert.Equal(report, File.ReadAllText(ProfilePath("sql-complete-correspondence.tsv")).Replace("\r\n", "\n", StringComparison.Ordinal));
        var original = File.ReadLines(ParityPath("profile-values.tsv")).Skip(1).Select(l => l.Split('\t')).ToDictionary(r => r[0]);
        var rows = report.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split('\t')).ToArray();
        Assert.Equal(977, rows.Length);
        Assert.Equal(577, rows.Select(r => r[0].Split('.')[0]).Distinct().Count());
        Assert.Equal(969, rows.Count(r => r[4] == "covered"));
        Assert.Equal(8, rows.Count(r => r[4] == "not_applicable"));
        foreach (var row in rows)
        {
            Assert.Equal(11, row.Length);
            Assert.Equal(original[row[0]][2], row[1]);
            Assert.Equal(original[row[0]][3], row[2]);
            Assert.Equal(original[row[0]][4], row[3]);
            if (row[4] == "covered")
            {
                Assert.Equal("native_alternative_not_xml_conversion", row[8]);
                Assert.NotEqual("-", row[6]);
                Assert.NotEqual("-", row[7]);
                JToken.Parse(row[6]);
                JToken.Parse(row[7]);
            }
            else Assert.Equal("excluded_by_user_decision", row[8]);
        }
        Assert.Throws<ArgumentException>(() => ProfileCorrespondenceReport.Create(new byte[] { 0 }, coverage));
        Assert.Throws<ArgumentException>(() => ProfileCorrespondenceReport.Create(snapshot, coverage.Replace("covered", "pending_semantics", StringComparison.Ordinal)));
    }

    public static IEnumerable<object[]> CorpusProfiles() => NativeFormattingPresets.Profiles.SelectMany(p =>
        File.ReadLines(ParityPath("corpus.tsv")).Skip(1).Select(l => l.Split('\t'))
            .Select(r => new object[] { p.Id, r[0], r[1].Replace("\\n", "\n", StringComparison.Ordinal) }));

    [Theory]
    [MemberData(nameof(CorpusProfiles))]
    public void Native_alternatives_cover_all_categories_and_preserve_tokens_and_idempotence(string id, string category, string sql)
    {
        var options = NativeFormattingPresets.Profiles.Single(p => p.Id == id).Options;
        var formatter = new ScriptDomSqlFormatter();
        var formatted = formatter.Format(sql, options, new FormatRequest());
        Assert.True(formatted.ParseSucceeded, id + " / " + category);
        Assert.DoesNotContain(formatted.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(formatted.Text, formatter.Format(formatted.Text, options, new FormatRequest()).Text);
        var parser = new ScriptDomSqlParser();
        var before = parser.Parse(sql, SqlDialectVersion.Auto);
        var after = parser.Parse(formatted.Text, SqlDialectVersion.Auto);
        Assert.True(after.ParseSucceeded);
        Assert.Equal(Tokens(before).Select(t => t.ToUpperInvariant()), Tokens(after).Select(t => t.ToUpperInvariant()));
        // All non-keyword text, including literal/quoted identifier contents, must stay exact.
        var preserve = options.With(keywords: new KeywordOptions(KeywordCase.Preserve));
        var stable = formatter.Format(sql, preserve, new FormatRequest());
        Assert.Equal(Tokens(before), Tokens(parser.Parse(stable.Text, SqlDialectVersion.Auto)));
    }

    private static IEnumerable<string> Tokens(SqlParseResult parsed) => parsed.Tokens
        .Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).Select(t => t.Text);
}
