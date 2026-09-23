using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Tests;

public sealed class DocRendererBehaviorTests
{
    private readonly DocRenderer _renderer = new();

    [Theory]
    [InlineData(7, "A B C D")]
    [InlineData(6, "A\nB C\nD")]
    [InlineData(3, "A\nB C\nD")]
    [InlineData(2, "A\nB\nC\nD")]
    public void NestedGroups_ChooseIndependentModes(int width, string expected)
    {
        var inner = Group(new TextDoc("B"), SoftLineDoc.Instance, new TextDoc("C"));
        var outer = Group(new TextDoc("A"), SoftLineDoc.Instance, inner, SoftLineDoc.Instance, new TextDoc("D"));

        Assert.Equal(expected, _renderer.Render(outer, new DocRenderOptions(maxLineWidth: width)));
    }

    [Theory]
    [InlineData(0, false, "A\nB\nC")]
    [InlineData(2, false, "A\n  B\n    C")]
    [InlineData(4, false, "A\n    B\n        C")]
    [InlineData(4, true, "A\n\tB\n\t\tC")]
    public void NestedIndents_AccumulateLevels(int indentWidth, bool useTabs, string expected)
    {
        var document = Concat(
            new TextDoc("A"),
            new IndentDoc(1, Concat(
                HardLineDoc.Instance,
                new TextDoc("B"),
                new IndentDoc(1, Concat(HardLineDoc.Instance, new TextDoc("C"))))));

        Assert.Equal(expected, _renderer.Render(document, new DocRenderOptions(indentWidth: indentWidth, useTabs: useTabs)));
    }

    [Theory]
    [InlineData(1, "ABCDEFGHIJ\nx")]
    [InlineData(5, "ABCDEFGHIJ\nx")]
    [InlineData(10, "ABCDEFGHIJ\nx")]
    [InlineData(12, "ABCDEFGHIJ x")]
    public void LongToken_IsNeverSplit(int width, string expected)
    {
        var document = Group(new TextDoc("ABCDEFGHIJ"), SoftLineDoc.Instance, new TextDoc("x"));

        Assert.Equal(expected, _renderer.Render(document, new DocRenderOptions(maxLineWidth: width)));
    }

    [Theory]
    [InlineData(1, "A\nB")]
    [InlineData(2, "A\nB")]
    [InlineData(3, "A B")]
    [InlineData(4, "A B")]
    public void Group_RespectsExactWidthBoundary(int width, string expected)
    {
        Assert.Equal(expected, _renderer.Render(Group(new TextDoc("A"), SoftLineDoc.Instance, new TextDoc("B")),
            new DocRenderOptions(maxLineWidth: width)));
    }

    [Fact]
    public void EmptyText_StaysEmptyWithFinalNewline()
    {
        Assert.Equal(string.Empty, _renderer.Render(new TextDoc(string.Empty), new DocRenderOptions(finalNewline: true)));
    }

    [Fact]
    public void EmptyConcat_StaysEmpty()
    {
        Assert.Equal(string.Empty, _renderer.Render(new ConcatDoc(Array.Empty<Doc>())));
    }

    [Fact]
    public void EmptyGroup_StaysEmpty()
    {
        Assert.Equal(string.Empty, _renderer.Render(Group(new TextDoc(string.Empty))));
    }

    [Fact]
    public void EmptyIndent_StaysEmpty()
    {
        Assert.Equal(string.Empty, _renderer.Render(new IndentDoc(2, new TextDoc(string.Empty))));
    }

    [Fact]
    public void EmptyIfBreak_StaysEmpty()
    {
        Assert.Equal(string.Empty, _renderer.Render(new IfBreakDoc(new TextDoc(string.Empty), new TextDoc(string.Empty))));
    }

    [Fact]
    public void SoftLine_OutsideGroupBreaks()
    {
        Assert.Equal("A\nB", _renderer.Render(Concat(new TextDoc("A"), SoftLineDoc.Instance, new TextDoc("B"))));
    }

    [Fact]
    public void ConsecutiveSoftLines_InFlatGroupProduceSpaces()
    {
        Assert.Equal("A  B", _renderer.Render(Group(new TextDoc("A"), SoftLineDoc.Instance,
            SoftLineDoc.Instance, new TextDoc("B")), new DocRenderOptions(maxLineWidth: 4)));
    }

    [Fact]
    public void ConsecutiveSoftLines_InBrokenGroupProduceBlankLine()
    {
        Assert.Equal("A\n\nB", _renderer.Render(Group(new TextDoc("A"), SoftLineDoc.Instance,
            SoftLineDoc.Instance, new TextDoc("B")), new DocRenderOptions(maxLineWidth: 3)));
    }

    [Fact]
    public void GeneratedTrailingSoftLine_IsTrimmedAtEnd()
    {
        Assert.Equal("A", _renderer.Render(Group(new TextDoc("A"), SoftLineDoc.Instance)));
    }

    [Fact]
    public void GeneratedTrailingSoftLine_IsTrimmedBeforeFinalNewline()
    {
        Assert.Equal("A\n", _renderer.Render(Group(new TextDoc("A"), SoftLineDoc.Instance),
            new DocRenderOptions(finalNewline: true)));
    }

    [Fact]
    public void HardLine_BreaksEvenInsideWideGroup()
    {
        Assert.Equal("A\nB", _renderer.Render(Group(new TextDoc("A"), HardLineDoc.Instance, new TextDoc("B")),
            new DocRenderOptions(maxLineWidth: 100)));
    }

    [Fact]
    public void ConsecutiveHardLines_ProduceBlankLine()
    {
        Assert.Equal("A\n\nB", _renderer.Render(Concat(new TextDoc("A"), HardLineDoc.Instance,
            HardLineDoc.Instance, new TextDoc("B"))));
    }

    [Fact]
    public void LeadingHardLine_DoesNotIndentBlankLine()
    {
        Assert.Equal("\n    A", _renderer.Render(new IndentDoc(1, Concat(HardLineDoc.Instance, new TextDoc("A")))));
    }

    [Fact]
    public void FinalNewline_DoesNotDuplicateHardLine()
    {
        Assert.Equal("A\n", _renderer.Render(Concat(new TextDoc("A"), HardLineDoc.Instance),
            new DocRenderOptions(finalNewline: true)));
    }

    [Fact]
    public void IfBreak_OutsideGroupChoosesBrokenBranch()
    {
        Assert.Equal("!", _renderer.Render(new IfBreakDoc(new TextDoc("!"), new TextDoc("?"))));
    }

    [Fact]
    public void IfBreak_InsideFlatGroupChoosesFlatBranch()
    {
        Assert.Equal("?", _renderer.Render(Group(new IfBreakDoc(new TextDoc("!"), new TextDoc("?")))));
    }

    [Fact]
    public void GroupFit_IncludesExistingColumn()
    {
        var document = Concat(new TextDoc("XX"), Group(new TextDoc("A"), SoftLineDoc.Instance, new TextDoc("B")));

        Assert.Equal("XXA\nB", _renderer.Render(document, new DocRenderOptions(maxLineWidth: 4)));
    }

    [Fact]
    public void GroupWithEmbeddedNewline_UsesBrokenBranch()
    {
        var document = Group(new TextDoc("A\r\nB"), new IfBreakDoc(new TextDoc("!"), new TextDoc("?")));

        Assert.Equal("A\r\nB!", _renderer.Render(document));
    }

    [Fact]
    public void GeneratedSpaceBeforeHardLine_IsTrimmed()
    {
        var document = Concat(Group(new TextDoc("A"), SoftLineDoc.Instance), HardLineDoc.Instance, new TextDoc("B"));

        Assert.Equal("A\nB", _renderer.Render(document));
    }

    [Fact]
    public void LiteralSpaceBeforeHardLine_IsPreserved()
    {
        Assert.Equal("A \nB", _renderer.Render(Concat(new TextDoc("A "), HardLineDoc.Instance, new TextDoc("B"))));
    }

    [Fact]
    public void LiteralLineEnding_IsNotConvertedByRenderOptions()
    {
        var document = Concat(new TextDoc("A\r\nB"), HardLineDoc.Instance, new TextDoc("C"));

        Assert.Equal("A\r\nB\nC", _renderer.Render(document, new DocRenderOptions(lineEnding: DocLineEnding.Lf)));
    }

    private static Doc Group(params Doc[] children) => new GroupDoc(new ConcatDoc(children));

    private static Doc Concat(params Doc[] children) => new ConcatDoc(children);
}
