using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class StrictParseBehaviorTests
{
    [Theory]
    [InlineData("select from")]
    [InlineData("select (")]
    [InlineData("insert into T values (")]
    [InlineData("update T set")]
    [InlineData("merge")]
    public void Invalid_or_incomplete_sql_is_returned_unchanged_with_error_diagnostics(string source)
    {
        var result = new ScriptDomSqlFormatter().Format(source,
            FormattingOptions.Default, new FormatRequest());

        Assert.False(result.ParseSucceeded);
        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Empty(result.Edits);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.Equal("TSF1000", diagnostic.Code);
            Assert.Equal(FormatterDiagnosticSeverity.Error, diagnostic.Severity);
        });
    }

    [Fact]
    public void A_parser_without_a_tree_or_diagnostics_still_produces_an_error()
    {
        var result = new ScriptDomSqlFormatter(new NoTreeParser()).Format(
            "select Id from T", FormattingOptions.Default, new FormatRequest());

        Assert.False(result.ParseSucceeded);
        Assert.False(result.Changed);
        Assert.Equal("select Id from T", result.Text);
        Assert.Equal("TSF1000", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Failed_validation_of_generated_sql_is_an_error_and_leaves_source_unchanged()
    {
        const string source = "select Id from T";
        var result = new ScriptDomSqlFormatter(new FailOnSecondParseParser()).Format(
            source, FormattingOptions.Default, new FormatRequest());

        Assert.True(result.ParseSucceeded); // The original SQL parsed successfully.
        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Empty(result.Edits);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TSF3001", diagnostic.Code);
        Assert.Equal(FormatterDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData(ParseFailureBehavior.Safe)]
    [InlineData(ParseFailureBehavior.TokenFallback)]
    public void Unsupported_parse_failure_modes_are_not_silently_treated_as_strict(
        ParseFailureBehavior behavior)
    {
        const string source = "select Id from T";
        var result = new ScriptDomSqlFormatter().Format(source,
            FormattingOptions.Default, new FormatRequest(parseFailureBehavior: behavior));

        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Empty(result.Edits);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TSF3002", diagnostic.Code);
        Assert.Equal(FormatterDiagnosticSeverity.Error, diagnostic.Severity);
    }

    private sealed class NoTreeParser : ISqlParser
    {
        public SqlParseResult Parse(string source, SqlDialectVersion dialect,
            CancellationToken cancellationToken = default) => new(source, dialect,
            SqlVersion.Sql180, null, Array.Empty<TSqlParserToken>(), Array.Empty<ParseDiagnostic>());
    }

    private sealed class FailOnSecondParseParser : ISqlParser
    {
        private readonly ScriptDomSqlParser _real = new();
        private int _calls;

        public SqlParseResult Parse(string source, SqlDialectVersion dialect,
            CancellationToken cancellationToken = default)
        {
            _calls++;
            if (_calls == 1) return _real.Parse(source, dialect, cancellationToken);
            return new SqlParseResult(source, dialect, SqlVersion.Sql180, null,
                Array.Empty<TSqlParserToken>(), new[]
                {
                    new ParseDiagnostic(1, "Generated SQL failed to parse.", 0, 1, 1)
                });
        }
    }
}
