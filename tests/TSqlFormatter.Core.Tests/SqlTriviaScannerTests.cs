using TSqlFormatter.Core.Parsing;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlTriviaScannerTests
{
    [Fact]
    public void Classifies_comment_chain_inline_and_detached_comments()
    {
        const string source = "-- first\n-- second\nSELECT Id, -- inline\n Name FROM T;\n\n-- detached\n\nSELECT 1;";
        var parsed = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto);

        var comments = new SqlTriviaScanner().Scan(parsed);

        Assert.Equal(4, comments.Count);
        Assert.Equal(new[]
        {
            SqlTriviaPlacement.Leading,
            SqlTriviaPlacement.Leading,
            SqlTriviaPlacement.Trailing,
            SqlTriviaPlacement.Standalone
        }, comments.Select(comment => comment.Placement));
        Assert.Equal("SELECT", parsed.Tokens[comments[0].AnchorTokenIndex!.Value].Text.ToUpperInvariant());
        Assert.Equal(comments[0].AnchorTokenIndex, comments[1].AnchorTokenIndex);
        Assert.Equal(",", parsed.Tokens[comments[2].AnchorTokenIndex!.Value].Text);
        Assert.Equal(SqlCommentKind.Line, comments[2].Kind);
        Assert.Equal(comments[2].Text, parsed.Tokens[comments[2].TokenIndex].Text);
        Assert.Null(comments[3].AnchorTokenIndex);
        foreach (var comment in comments)
        {
            Assert.Equal(comment.Text, source.Substring(comment.Span.StartOffset, comment.Span.Length));
        }
    }

    [Theory]
    [InlineData("/* before */ SELECT 1", SqlTriviaPlacement.Leading)]
    [InlineData("SELECT /* after */ 1", SqlTriviaPlacement.Trailing)]
    [InlineData("/* alone */", SqlTriviaPlacement.Standalone)]
    public void Classifies_block_comments(string source, SqlTriviaPlacement expected)
    {
        var parsed = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto);
        var comment = Assert.Single(new SqlTriviaScanner().Scan(parsed));

        Assert.Equal(expected, comment.Placement);
        Assert.Equal(SqlCommentKind.Block, comment.Kind);
    }

    [Fact]
    public void Empty_sql_has_no_comments()
    {
        var parsed = new ScriptDomSqlParser().Parse(string.Empty, SqlDialectVersion.Auto);

        Assert.Empty(new SqlTriviaScanner().Scan(parsed));
    }

    [Fact]
    public void CrLf_keeps_adjacent_leading_comment()
    {
        var parsed = new ScriptDomSqlParser().Parse("-- note\r\nSELECT 1", SqlDialectVersion.Auto);
        var comment = Assert.Single(new SqlTriviaScanner().Scan(parsed));

        Assert.Equal(SqlTriviaPlacement.Leading, comment.Placement);
        Assert.Equal("SELECT", parsed.Tokens[comment.AnchorTokenIndex!.Value].Text.ToUpperInvariant());
    }
}
