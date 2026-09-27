using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class StatementFormattingTests
{
    private readonly ScriptDomSqlFormatter formatter = new();

    [Fact]
    public void Formats_only_statement_containing_caret()
    {
        const string source = "select a from T; select b from U;";
        int caret = source.IndexOf("b from", StringComparison.Ordinal);
        var result = formatter.Format(source, FormattingOptions.Default,
            new FormatRequest(FormatScope.Statement, new SqlTextSpan(caret, 0)));

        Assert.True(result.ParseSucceeded);
        Assert.True(result.Changed);
        Assert.Single(result.Edits);
        Assert.StartsWith("select a from T;", result.Text);
        Assert.Contains("SELECT b", result.Text);
    }

    [Fact]
    public void Refuses_caret_outside_document()
    {
        const string source = "select 1;";
        var result = formatter.Format(source, FormattingOptions.Default,
            new FormatRequest(FormatScope.Statement, new SqlTextSpan(source.Length + 1, 0)));

        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF3004");
    }

    [Fact]
    public void Refuses_script_without_statements()
    {
        const string source = "-- comment";
        var result = formatter.Format(source, FormattingOptions.Default,
            new FormatRequest(FormatScope.Statement, new SqlTextSpan(2, 0)));

        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF3004");
    }
}
