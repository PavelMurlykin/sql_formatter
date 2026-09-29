using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlLayoutPrimitivesTests
{
    private readonly DocRenderer renderer = new();

    [Fact]
    public void Scoped_policy_uses_most_specific_non_null_value_per_field()
    {
        var global = new SqlLayoutPolicy(before: LayoutBreak.Auto, list: LayoutList.Auto,
            comma: LayoutComma.Trailing, indentLevels: 1, align: false);
        var statement = new SqlLayoutPolicy(before: LayoutBreak.Always, indentLevels: 2);
        var clause = new SqlLayoutPolicy(list: LayoutList.OnePerLine, align: true);
        var local = new SqlLayoutPolicy(before: LayoutBreak.Never, comma: LayoutComma.Leading);

        var result = SqlLayoutPolicy.Resolve(global, statement, clause, local);

        Assert.Equal(LayoutBreak.Never, result.Before);
        Assert.Equal(LayoutList.OnePerLine, result.List);
        Assert.Equal(LayoutComma.Leading, result.Comma);
        Assert.Equal(2, result.IndentLevels);
        Assert.True(result.Align);
        Assert.Equal(LayoutBreak.Auto, global.Before);
        Assert.Null(clause.Comma);
    }

    [Theory]
    [InlineData(LayoutBreak.Never, "A B")]
    [InlineData(LayoutBreak.Auto, "A B")]
    [InlineData(LayoutBreak.Always, "A\nB")]
    public void Break_preferences_have_distinct_results(LayoutBreak preference, string expected)
    {
        var doc = new GroupDoc(new ConcatDoc(new Doc[]
        { new TextDoc("A"), SqlLayoutPrimitives.Break(preference), new TextDoc("B") }));
        Assert.Equal(expected, renderer.Render(doc));
    }

    [Theory]
    [InlineData(LayoutComma.Trailing, "A,\nB,\nC")]
    [InlineData(LayoutComma.Leading, "A\n, B\n, C")]
    public void Comma_lists_support_both_positions(LayoutComma placement, string expected)
    {
        var items = new Doc[] { new TextDoc("A"), new TextDoc("B"), new TextDoc("C") };
        var doc = SqlLayoutPrimitives.CommaSeparated(items, LayoutList.OnePerLine, placement);
        Assert.Equal(expected, renderer.Render(doc));
    }

    [Fact]
    public void Auto_list_and_parentheses_fit_or_break_without_changing_comment_text()
    {
        var list = SqlLayoutPrimitives.CommaSeparated(new Doc[]
        { new TextDoc("A /*keep*/"), new TextDoc("B") }, LayoutList.Auto);
        var doc = new GroupDoc(SqlLayoutPrimitives.Parenthesize(list,
            LayoutBreak.Auto, LayoutBreak.Auto));

        Assert.Equal("( A /*keep*/, B )", renderer.Render(doc, new DocRenderOptions(maxLineWidth: 30)));
        Assert.Equal("(\n    A /*keep*/,\n    B\n)", renderer.Render(doc,
            new DocRenderOptions(maxLineWidth: 12)));
        Assert.Equal("(A)", renderer.Render(SqlLayoutPrimitives.Parenthesize(new TextDoc("A"),
            LayoutBreak.Never, LayoutBreak.Never)));
    }

    [Fact]
    public void Alignment_falls_back_on_comments_tabs_and_width()
    {
        var safe = new[] { ("Id", "AS Key"), ("LongName", "AS Name") };
        Assert.Equal(new[] { "Id       AS Key", "LongName AS Name" },
            SqlLayoutPrimitives.AlignPairs(safe, 40));
        Assert.Null(SqlLayoutPrimitives.AlignPairs(safe, 10));
        Assert.Null(SqlLayoutPrimitives.AlignPairs(new[] { ("Id /*comment*/", "AS X"),
            ("Name", "AS Y") }, 80));
        Assert.Null(SqlLayoutPrimitives.AlignPairs(new[] { ("Id\t", "AS X"),
            ("Name", "AS Y") }, 80));
    }
}
