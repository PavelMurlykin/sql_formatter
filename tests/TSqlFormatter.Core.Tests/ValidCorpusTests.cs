using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class ValidCorpusTests
{
    [Fact]
    public async Task Representative_sql_remains_parseable_idempotent_and_token_safe()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ValidCorpus");
        var files = Directory.GetFiles(directory, "*.sql").OrderBy(path => path,
            StringComparer.Ordinal).ToArray();
        Assert.True(files.Length >= 6, "The valid regression corpus must be copied to test output.");

        var formatter = new ScriptDomSqlFormatter();
        var parser = new ScriptDomSqlParser();
        foreach (var path in files)
        {
            var source = await File.ReadAllTextAsync(path);
            var name = Path.GetFileName(path);
            var parsedSource = parser.Parse(source, SqlDialectVersion.Auto);
            Assert.True(parsedSource.ParseSucceeded, $"{name}: input did not parse.");

            var first = formatter.Format(source, FormattingOptions.Default, new FormatRequest());
            Assert.True(first.ParseSucceeded, $"{name}: formatter did not parse input.");
            Assert.DoesNotContain(first.Diagnostics, diagnostic =>
                diagnostic.Severity == FormatterDiagnosticSeverity.Error);
            var parsedOutput = parser.Parse(first.Text, SqlDialectVersion.Auto);
            Assert.True(parsedOutput.ParseSucceeded, $"{name}: output did not parse.");
            Assert.Equal(ProtectedTokens(parsedSource), ProtectedTokens(parsedOutput));
            Assert.Equal(MeaningfulTokens(parsedSource), MeaningfulTokens(parsedOutput));

            var second = formatter.Format(first.Text, FormattingOptions.Default, new FormatRequest());
            Assert.True(second.ParseSucceeded, $"{name}: second pass did not parse.");
            Assert.Equal(first.Text, second.Text);
            Assert.False(second.Changed);
        }
    }

    [Fact]
    public async Task Unsupported_predicate_operators_are_not_rewritten()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ValidCorpus", "unsupported-predicates.sql");
        var source = await File.ReadAllTextAsync(path);

        var result = new ScriptDomSqlFormatter().Format(source, FormattingOptions.Default,
            new FormatRequest());

        Assert.True(result.ParseSucceeded);
        Assert.Contains("LIKE N'A%' AND DeletedAt IS NULL", result.Text);
    }

    private static string[] ProtectedTokens(SqlParseResult parsed) => parsed.Tokens
        .Where(token => token.TokenType is TSqlTokenType.SingleLineComment
            or TSqlTokenType.MultilineComment or TSqlTokenType.AsciiStringLiteral
            or TSqlTokenType.UnicodeStringLiteral)
        .Select(token => $"{token.TokenType}:{token.Text}")
        .ToArray();

    private static string[] MeaningfulTokens(SqlParseResult parsed) => parsed.Tokens
        .Where(token => token.TokenType is not TSqlTokenType.WhiteSpace
            and not TSqlTokenType.EndOfFile)
        .Select(token => $"{token.TokenType}:" +
            (token.IsKeyword() && token.TokenType != TSqlTokenType.Identifier
                ? token.Text.ToUpperInvariant() : token.Text))
        .ToArray();
}
