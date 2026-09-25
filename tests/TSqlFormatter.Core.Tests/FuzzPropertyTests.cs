using System.Text;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class FuzzPropertyTests
{
    [Fact]
    public void Generated_valid_selects_remain_parseable_idempotent_and_match_reported_edits()
    {
        const int seed = 106005;
        var random = new Random(seed);
        var formatter = new ScriptDomSqlFormatter();
        var parser = new ScriptDomSqlParser();
        for (var iteration = 0; iteration < 120; iteration++)
        {
            var source = GenerateSelect(random);
            var options = new FormattingOptions(
                general: new GeneralOptions(maxLineWidth: random.Next(24, 101),
                    lineEnding: random.Next(2) == 0 ? DocLineEnding.Lf : DocLineEnding.CrLf),
                indent: new IndentOptions(random.Next(1, 5)),
                keywords: new KeywordOptions(random.Next(2) == 0 ? KeywordCase.Upper : KeywordCase.Lower),
                select: new SelectOptions(random.Next(2) == 0
                    ? SelectColumnLayout.Auto : SelectColumnLayout.OnePerLine));

            var first = RunCase(() => formatter.Format(source, options, new FormatRequest()),
                seed, iteration, source);
            Assert.True(first.ParseSucceeded, $"Seed {seed}, case {iteration}: {source}");
            Assert.True(parser.Parse(first.Text, SqlDialectVersion.Auto).ParseSucceeded,
                $"Seed {seed}, case {iteration}: output {first.Text}");
            Assert.Equal(first.Text, ApplyEdits(source, first.Edits));

            var second = RunCase(() => formatter.Format(first.Text, options, new FormatRequest()),
                seed, iteration, first.Text);
            Assert.True(string.Equals(first.Text, second.Text, StringComparison.Ordinal),
                $"Seed {seed}, case {iteration}: formatting was not idempotent. Input: {source}");
            Assert.False(second.Changed);
        }
    }

    [Fact]
    public void Arbitrary_short_sql_never_produces_partial_strict_output_on_parse_failure()
    {
        const int seed = 106006;
        const string alphabet = "abcSELECT0123/*-'[]();,@ \n\t\r";
        var random = new Random(seed);
        var formatter = new ScriptDomSqlFormatter();
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var source = new string(Enumerable.Range(0, random.Next(121))
                .Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

            var result = RunCase(() => formatter.Format(source, FormattingOptions.Default,
                new FormatRequest()), seed, iteration, source);
            if (!result.ParseSucceeded)
            {
                Assert.Equal(source, result.Text);
                Assert.False(result.Changed);
                Assert.Empty(result.Edits);
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF1000");
            }
            else
            {
                Assert.Equal(result.Text, ApplyEdits(source, result.Edits));
            }
        }
    }

    [Fact]
    public void Arbitrary_json_returns_options_or_configuration_diagnostics()
    {
        const int seed = 106007;
        const string alphabet = "{}[]\"':,0123456789nulltruefalsegeneralversion \n";
        var random = new Random(seed);
        var serializer = new SqlFormatterConfigurationSerializer();
        for (var iteration = 0; iteration < 150; iteration++)
        {
            var json = new string(Enumerable.Range(0, random.Next(100))
                .Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

            var result = RunCase(() => serializer.Parse(json), seed, iteration, json);
            Assert.Equal(result.Succeeded, result.Options is not null);
            if (!result.Succeeded)
            {
                Assert.NotEmpty(result.Diagnostics);
                Assert.All(result.Diagnostics, diagnostic => Assert.Equal("TSF2000", diagnostic.Code));
            }
        }
    }

    private static string GenerateSelect(Random random)
    {
        var columnCount = random.Next(1, 5);
        var separator = random.Next(2) == 0 ? "," : ", ";
        var columns = string.Join(separator, Enumerable.Range(0, columnCount)
            .Select(index => $"C{index + 1}"));
        var keyword = random.Next(2) == 0 ? "SELECT" : "select";
        var from = random.Next(2) == 0 ? "FROM" : "from";
        var where = random.Next(2) == 0 ? "WHERE" : "where";
        var gap = random.Next(2) == 0 ? " " : "  ";
        return $"{keyword}{gap}{columns}{gap}{from}{gap}dbo.T{gap}{where}{gap}C1 = {random.Next(100)};";
    }

    private static string ApplyEdits(string source, IReadOnlyList<TextEdit> edits)
    {
        var output = new StringBuilder();
        var cursor = 0;
        foreach (var edit in edits)
        {
            Assert.True(edit.Span.StartOffset >= cursor);
            Assert.True(edit.Span.EndOffset <= source.Length);
            output.Append(source, cursor, edit.Span.StartOffset - cursor);
            output.Append(edit.NewText);
            cursor = edit.Span.EndOffset;
        }
        output.Append(source, cursor, source.Length - cursor);
        return output.ToString();
    }

    private static T RunCase<T>(Func<T> operation, int seed, int iteration, string input)
    {
        try
        {
            return operation();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Seed {seed}, case {iteration}, input: {input}", exception);
        }
    }
}
