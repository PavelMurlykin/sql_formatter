using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Tests;

public sealed class LayoutDocTests
{
    [Fact]
    public void TextDoc_PreservesLiteralTextIncludingEmptyAndNewlines()
    {
        Assert.Equal(string.Empty, new TextDoc(string.Empty).Text);
        Assert.Equal("'first\r\nsecond'", new TextDoc("'first\r\nsecond'").Text);
        Assert.Throws<ArgumentNullException>(() => new TextDoc(null!));
    }

    [Fact]
    public void ConcatDoc_PreservesOrderAndCopiesChildren()
    {
        var first = new TextDoc("SELECT");
        var second = SoftLineDoc.Instance;
        var source = new List<Doc> { first, second };

        var concat = new ConcatDoc(source);
        source[0] = new TextDoc("CHANGED");
        source.Add(HardLineDoc.Instance);

        Assert.Equal(2, concat.Children.Count);
        Assert.Same(first, concat.Children[0]);
        Assert.Same(second, concat.Children[1]);
        Assert.Throws<NotSupportedException>(() => ((IList<Doc>)concat.Children)[0] = second);
    }

    [Fact]
    public void ConcatDoc_AcceptsEmptySequenceAndRejectsNulls()
    {
        Assert.Empty(new ConcatDoc(Array.Empty<Doc>()).Children);
        Assert.Throws<ArgumentNullException>(() => new ConcatDoc(null!));
        Assert.Throws<ArgumentException>(() => new ConcatDoc(new Doc[] { new TextDoc("x"), null! }));
    }

    [Fact]
    public void LineNodes_AreDistinctSingletons()
    {
        Assert.Same(SoftLineDoc.Instance, SoftLineDoc.Instance);
        Assert.Same(HardLineDoc.Instance, HardLineDoc.Instance);
        Assert.NotSame(SoftLineDoc.Instance, HardLineDoc.Instance);
    }

    [Fact]
    public void IndentDoc_StoresNonnegativeLevelsAndContent()
    {
        var content = new TextDoc("Id");

        var indent = new IndentDoc(2, content);

        Assert.Equal(2, indent.Levels);
        Assert.Same(content, indent.Content);
        Assert.Equal(0, new IndentDoc(0, content).Levels);
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndentDoc(-1, content));
        Assert.Throws<ArgumentNullException>(() => new IndentDoc(1, null!));
    }

    [Fact]
    public void GroupDoc_StoresContentAndRejectsNull()
    {
        var content = new TextDoc("SELECT Id");

        Assert.Same(content, new GroupDoc(content).Content);
        Assert.Throws<ArgumentNullException>(() => new GroupDoc(null!));
    }

    [Fact]
    public void IfBreakDoc_StoresBothAlternativesAndRejectsNulls()
    {
        var broken = new TextDoc(",");
        var flat = new TextDoc(string.Empty);

        var conditional = new IfBreakDoc(broken, flat);

        Assert.Same(broken, conditional.Broken);
        Assert.Same(flat, conditional.Flat);
        Assert.Throws<ArgumentNullException>(() => new IfBreakDoc(null!, flat));
        Assert.Throws<ArgumentNullException>(() => new IfBreakDoc(broken, null!));
    }

    [Fact]
    public void DocumentNodes_CanComposeNestedTree()
    {
        var document = new GroupDoc(
            new ConcatDoc(new Doc[]
            {
                new TextDoc("SELECT"),
                SoftLineDoc.Instance,
                new IndentDoc(1, new ConcatDoc(new Doc[]
                {
                    new TextDoc("Id"),
                    new IfBreakDoc(new TextDoc(","), new TextDoc(string.Empty)),
                    HardLineDoc.Instance,
                    new TextDoc("Name")
                }))
            }));

        var root = Assert.IsType<ConcatDoc>(document.Content);
        Assert.Equal(3, root.Children.Count);
        Assert.IsType<IndentDoc>(root.Children[2]);
    }
}
