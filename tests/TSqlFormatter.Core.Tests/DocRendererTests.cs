using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Tests;

public sealed class DocRendererTests
{
    private readonly DocRenderer _renderer = new();

    [Fact]
    public void Group_FitsAtWidthBoundaryAndBreaksBelowIt()
    {
        var document = SelectDocument();

        Assert.Equal("SELECT Id, Name", _renderer.Render(document, new DocRenderOptions(maxLineWidth: 15)));
        Assert.Equal("SELECT\n    Id,\n    Name", _renderer.Render(document, new DocRenderOptions(maxLineWidth: 14)));
    }

    [Theory]
    [InlineData(DocLineEnding.Lf, "A\nB")]
    [InlineData(DocLineEnding.CrLf, "A\r\nB")]
    [InlineData(DocLineEnding.Cr, "A\rB")]
    public void HardLine_UsesSelectedLineEnding(DocLineEnding lineEnding, string expected)
    {
        var document = new ConcatDoc(new Doc[] { new TextDoc("A"), HardLineDoc.Instance, new TextDoc("B") });

        Assert.Equal(expected, _renderer.Render(document, new DocRenderOptions(lineEnding: lineEnding)));
    }

    [Fact]
    public void Indent_CanUseTabs()
    {
        var document = new ConcatDoc(new Doc[]
        {
            new TextDoc("A"),
            new IndentDoc(2, new ConcatDoc(new Doc[] { HardLineDoc.Instance, new TextDoc("B") }))
        });

        Assert.Equal("A\n\t\tB", _renderer.Render(document, new DocRenderOptions(useTabs: true)));
    }

    [Fact]
    public void FinalNewline_IsAddedOnlyWhenNeeded()
    {
        var options = new DocRenderOptions(lineEnding: DocLineEnding.CrLf, finalNewline: true);

        Assert.Equal("A\r\n", _renderer.Render(new TextDoc("A"), options));
        Assert.Equal("A\r\n", _renderer.Render(new TextDoc("A\r\n"), options));
        Assert.Equal(string.Empty, _renderer.Render(new TextDoc(string.Empty), options));
    }

    [Fact]
    public void IfBreak_UsesCurrentGroupMode()
    {
        var document = new GroupDoc(new ConcatDoc(new Doc[]
        {
            new TextDoc("A"), SoftLineDoc.Instance, new TextDoc("B"),
            new IfBreakDoc(new TextDoc("!"), new TextDoc("?"))
        }));

        Assert.Equal("A B?", _renderer.Render(document, new DocRenderOptions(maxLineWidth: 4)));
        Assert.Equal("A\nB!", _renderer.Render(document, new DocRenderOptions(maxLineWidth: 3)));
    }

    [Fact]
    public void TextDoc_PreservesEmbeddedLineEnding()
    {
        Assert.Equal("A\r\nB", _renderer.Render(new TextDoc("A\r\nB"), new DocRenderOptions(lineEnding: DocLineEnding.Lf)));
    }

    [Fact]
    public void InvalidOptionsAndNullDocument_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => _renderer.Render(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocRenderOptions(maxLineWidth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocRenderOptions(indentWidth: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocRenderOptions(lineEnding: (DocLineEnding)99));
    }

    private static Doc SelectDocument()
    {
        return new GroupDoc(new ConcatDoc(new Doc[]
        {
            new TextDoc("SELECT"),
            new IndentDoc(1, new ConcatDoc(new Doc[]
            {
                SoftLineDoc.Instance,
                new TextDoc("Id,"),
                SoftLineDoc.Instance,
                new TextDoc("Name")
            }))
        }));
    }
}
