using Newtonsoft.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.GoldenTests;

public sealed class ParityGoldenTests
{
    public static IEnumerable<object[]> Cases => File.ReadLines(Path.Combine(AppContext.BaseDirectory, "parity-golden.tsv"))
        .Skip(1).Select(l => l.Split('\t')).Select(r => new object[]
        { r[0], r[1], JsonConvert.DeserializeObject<string>(r[2])!, JsonConvert.DeserializeObject<string>(r[3])! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Both_native_profile_alternatives_match_the_reviewed_golden_and_are_stable(string profileId, string name, string sql, string expected)
    {
        Assert.NotEmpty(name);
        var options = NativeFormattingPresets.Profiles.Single(p => p.Id == profileId).Options;
        var formatter = new ScriptDomSqlFormatter();
        var result = formatter.Format(sql, options, new FormatRequest());
        Assert.True(result.ParseSucceeded);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(expected, result.Text);
        var second = formatter.Format(result.Text, options, new FormatRequest());
        Assert.Equal(expected, second.Text);
        Assert.False(second.Changed);
        var parser = new ScriptDomSqlParser();
        var original = parser.Parse(sql, SqlDialectVersion.Auto);
        var preserved = formatter.Format(sql, options.With(keywords: new KeywordOptions(KeywordCase.Preserve)), new FormatRequest());
        var parsed = parser.Parse(preserved.Text, SqlDialectVersion.Auto);
        Assert.True(original.ParseSucceeded);
        Assert.True(preserved.ParseSucceeded);
        Assert.True(parsed.ParseSucceeded);
        static IEnumerable<string> Tokens(SqlParseResult p) => p.Tokens
            .Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).Select(t => t.Text);
        Assert.Equal(Tokens(original), Tokens(parsed));
    }

    [Fact]
    public void Both_alternatives_have_each_of_twenty_categories_and_all_edge_cases()
    {
        var cases = Cases.ToArray();
        Assert.Equal(82, cases.Length);
        string[] names = new[] { "parity-inventory-corpus.tsv", "parity-edge-corpus.tsv" }
            .SelectMany(file => File.ReadLines(Path.Combine(AppContext.BaseDirectory, file)).Skip(1))
            .Select(line => line.Split('\t')[0]).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(41, names.Length);
        Assert.Equal(41, names.Distinct(StringComparer.Ordinal).Count());
        foreach (var group in cases.GroupBy(c => (string)c[0]))
        {
            Assert.Equal(41, group.Count());
            Assert.Equal(names, group.Select(c => (string)c[1]).OrderBy(name => name, StringComparer.Ordinal));
        }
        Assert.Equal(NativeFormattingPresets.Profiles.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal),
            cases.Select(c => (string)c[0]).Distinct().OrderBy(id => id, StringComparer.Ordinal));
    }
}
