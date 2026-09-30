using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

internal static class ParityTestSupport
{
    public static FormattingOptions Options(int width = 100) => FormattingOptions.Default.With(
        general: new GeneralOptions(width, DocLineEnding.Lf), keywords: new KeywordOptions(KeywordCase.Preserve));
    public static (string, RuleValue) Choice(string key, string value) => (key, RuleValue.FromChoice(value));
    public static (string, RuleValue) Indent(string key, int offset = 1, bool newlineOnly = true,
        string style = "relative", bool transparent = false) =>
        (key, RuleValue.FromIndent(new IndentRule(true, offset, newlineOnly, style, transparent)));
    public static string Format(string sql, params (string Key, RuleValue Value)[] rules) => Format(sql, Options(), rules);
    public static string Format(string sql, FormattingOptions options, params (string Key, RuleValue Value)[] rules)
    {
        var values = new RuleOptions(RuleCatalog.Default);
        foreach (var (key, value) in rules) values = values.With(key, value);
        options = options.With(rules: values);
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded, string.Join("; ", first.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        var parser = new ScriptDomSqlParser();
        var before = parser.Parse(sql, SqlDialectVersion.Auto, CancellationToken.None);
        var after = parser.Parse(first.Text, SqlDialectVersion.Auto, CancellationToken.None);
        Assert.True(after.ParseSucceeded);
        static IEnumerable<string> Tokens(SqlParseResult parsed) => parsed.Tokens.Where(t =>
            t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).Select(t => t.Text);
        Assert.Equal(Tokens(before), Tokens(after));
        return first.Text;
    }
    public static string[][] Ledger(string stage) => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,
        "SqlCompleteParity", "coverage.tsv")).Skip(1).Select(line => line.Split('\t')).Where(row => row[1] == stage).ToArray();
    public static void CheckLedger(string stage, int count, string evidence)
    {
        var rows = Ledger(stage);
        Assert.Equal(count, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal("covered", row[2]);
            Assert.Contains(row[3], RuleCatalog.Default.Definitions.Keys);
            Assert.StartsWith(evidence + ".", row[4]);
            Assert.NotEqual("-", row[5]);
        });
    }
}
