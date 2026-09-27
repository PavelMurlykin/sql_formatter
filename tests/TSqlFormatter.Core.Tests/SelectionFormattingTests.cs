using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SelectionFormattingTests
{
    private readonly ScriptDomSqlFormatter formatter = new();

    [Fact]
    public void Formats_only_statement_containing_selected_token()
    {
        const string source = "select a from T; select b from U;";
        var result = formatter.Format(source, FormattingOptions.Default,
            new FormatRequest(FormatScope.Selection, new SqlTextSpan(0, 6)));

        Assert.True(result.ParseSucceeded);
        Assert.True(result.Changed);
        Assert.Single(result.Edits);
        Assert.StartsWith("SELECT a", result.Text);
        Assert.EndsWith("select b from U;", result.Text);
    }

    [Fact]
    public void Refuses_selection_that_cuts_string_literal()
    {
        const string source = "select 'abc' from T;";
        var result = formatter.Format(source, FormattingOptions.Default,
            new FormatRequest(FormatScope.Selection, new SqlTextSpan(9, 1)));

        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF3003");
    }

    [Fact]
    public void Refuses_selection_across_statements()
    {
        const string source = "select 1; select 2;";
        var result = formatter.Format(source, FormattingOptions.Default,
            new FormatRequest(FormatScope.Selection, new SqlTextSpan(0, source.Length)));

        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF3003");
    }
}
