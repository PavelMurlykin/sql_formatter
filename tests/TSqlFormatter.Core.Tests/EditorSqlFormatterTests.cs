using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class EditorSqlFormatterTests
{
    [Theory]
    [InlineData("select a,b from t; select c,d from u;", "select a,b from t;")]
    [InlineData("select a,b from t; select c,d from u; select e from v;", "select a,b from t; select c,d from u;")]
    [InlineData("select a,b from t where a=1 and b=2;", "where a=1 and b=2")]
    [InlineData("select a,b from t;", "a,b")]
    [InlineData("-- before\r\nselect a,b from t;\r\n-- after", "select a,b from t;")]
    public void Selection_edits_only_its_exact_bounds_and_is_repeatable(string sql, string selected)
    {
        int start = sql.IndexOf(selected, StringComparison.Ordinal);
        var span = new SqlTextSpan(start, selected.Length);
        var formatter = new EditorSqlFormatter();
        var result = formatter.Format(sql, FormattingOptions.Default, span);
        Assert.True(result.ParseSucceeded);
        Assert.True(result.Changed, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var edit = Assert.Single(result.Edits);
        Assert.InRange(edit.Span.StartOffset, span.StartOffset, span.EndOffset);
        Assert.InRange(edit.Span.EndOffset, span.StartOffset, span.EndOffset);
        Assert.StartsWith(sql.Substring(0, span.StartOffset), result.Text);
        Assert.EndsWith(sql.Substring(span.EndOffset), result.Text);
        Assert.True(new ScriptDomSqlParser().Parse(result.Text, SqlDialectVersion.Auto).ParseSucceeded);
        var second = formatter.Format(result.Text, FormattingOptions.Default, new SqlTextSpan(edit.Span.StartOffset, edit.NewText.Length));
        Assert.Equal(result.Text, second.Text);
    }

    [Fact]
    public void No_selection_formats_document_and_repeated_calls_remain_valid()
    {
        var formatter = new EditorSqlFormatter();
        var first = formatter.Format("select a,b from t;", FormattingOptions.Default);
        Assert.True(first.Changed);
        Assert.Equal(first.Text, formatter.Format(first.Text, FormattingOptions.Default).Text);
        Assert.Equal(first.Text, formatter.Format(first.Text, FormattingOptions.Default, new SqlTextSpan(0, 0)).Text);
    }

    [Fact]
    public void Token_cut_and_invalid_sql_leave_source_untouched()
    {
        const string sql = "select a from t;";
        var result = new EditorSqlFormatter().Format(sql, FormattingOptions.Default, new SqlTextSpan(1, 5));
        Assert.False(result.Changed); Assert.Empty(result.Edits); Assert.Equal("TSF3003", Assert.Single(result.Diagnostics).Code);
        Assert.False(new EditorSqlFormatter().Format("SELECT FROM;", FormattingOptions.Default).Changed);
    }

    [Fact]
    public void Standalone_selected_statements_can_format_with_invalid_text_outside_selection()
    {
        const string sql = "select a,b from t;\nSELECT FROM;";
        var result = new EditorSqlFormatter().Format(sql, FormattingOptions.Default, new SqlTextSpan(0, 18));
        Assert.True(result.Changed); Assert.EndsWith("\nSELECT FROM;", result.Text);
    }
}
