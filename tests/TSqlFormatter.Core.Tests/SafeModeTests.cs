using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class SafeModeTests
{
    private static readonly FormatRequest SafeRequest = new(
        parseFailureBehavior: ParseFailureBehavior.Safe);

    [Fact]
    public void Safe_mode_formats_valid_statements_around_invalid_sql()
    {
        const string source = "select 1;\nselect from;\nselect 2;";

        var result = new ScriptDomSqlFormatter().Format(source,
            FormattingOptions.Default, SafeRequest);

        Assert.False(result.ParseSucceeded);
        Assert.True(result.Changed);
        Assert.Contains("SELECT 1;", result.Text);
        Assert.Contains("select from;", result.Text);
        Assert.Contains("SELECT 2;", result.Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF1000");
        Assert.All(result.Edits, edit => Assert.DoesNotContain("select from", edit.NewText));
    }

    [Fact]
    public void Safe_mode_handles_fully_valid_sql_like_strict_mode()
    {
        const string source = "select Id from T";
        var formatter = new ScriptDomSqlFormatter();

        var safe = formatter.Format(source, FormattingOptions.Default, SafeRequest);
        var strict = formatter.Format(source, FormattingOptions.Default, new FormatRequest());

        Assert.True(safe.ParseSucceeded);
        Assert.Equal(strict.Text, safe.Text);
        Assert.Equal(strict.Changed, safe.Changed);
    }

    [Fact]
    public void Safe_mode_does_not_change_an_invalid_statement_without_valid_neighbors()
    {
        const string source = "select from;";

        var result = new ScriptDomSqlFormatter().Format(source,
            FormattingOptions.Default, SafeRequest);

        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
        Assert.Empty(result.Edits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF1000");
    }
}
