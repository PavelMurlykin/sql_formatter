using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlLineMapTests
{
    [Fact]
    public void EmptySource_HasOnePositionAtZero()
    {
        var map = new SqlLineMap(string.Empty);

        Assert.Equal(1, map.LineCount);
        AssertPosition(map, 0, 1, 1);
        Assert.Equal(0, map.GetOffset(1, 1));
    }

    [Fact]
    public void LfLineEndings_MapNewlineAndFinalEmptyLine()
    {
        var map = new SqlLineMap("a\nb\n");

        Assert.Equal(3, map.LineCount);
        AssertPosition(map, 0, 1, 1);
        AssertPosition(map, 1, 1, 2);
        AssertPosition(map, 2, 2, 1);
        AssertPosition(map, 3, 2, 2);
        AssertPosition(map, 4, 3, 1);
        Assert.Equal(4, map.GetOffset(3, 1));
    }

    [Fact]
    public void CrLfLineEndings_KeepBothCodeUnitsOnPreviousLine()
    {
        var map = new SqlLineMap("a\r\nb\r\n");

        Assert.Equal(3, map.LineCount);
        AssertPosition(map, 0, 1, 1);
        AssertPosition(map, 1, 1, 2);
        AssertPosition(map, 2, 1, 3);
        AssertPosition(map, 3, 2, 1);
        AssertPosition(map, 4, 2, 2);
        AssertPosition(map, 5, 2, 3);
        AssertPosition(map, 6, 3, 1);
        Assert.Equal(2, map.GetOffset(1, 3));
        Assert.Equal(3, map.GetOffset(2, 1));
    }

    [Fact]
    public void LoneCrAndMixedLineEndings_AreRecognized()
    {
        var map = new SqlLineMap("a\rb\nc\r\nd");

        Assert.Equal(4, map.LineCount);
        AssertPosition(map, 2, 2, 1);
        AssertPosition(map, 4, 3, 1);
        AssertPosition(map, 7, 4, 1);
    }

    [Fact]
    public void UnicodeColumns_AreUtf16CodeUnits()
    {
        var map = new SqlLineMap("A😀B\nЖ");

        Assert.Equal(2, map.LineCount);
        AssertPosition(map, 1, 1, 2);
        AssertPosition(map, 2, 1, 3);
        AssertPosition(map, 3, 1, 4);
        AssertPosition(map, 5, 2, 1);
        AssertPosition(map, 6, 2, 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SELECT 1;\n")]
    [InlineData("a\r\nb\nc\rd")]
    [InlineData("😀\r\nЖ")]
    public void EveryOffset_RoundTripsThroughPosition(string source)
    {
        var map = new SqlLineMap(source);

        for (var offset = 0; offset <= source.Length; offset++)
        {
            var position = map.GetLinePosition(offset);
            Assert.Equal(offset, map.GetOffset(position));
        }
    }

    [Fact]
    public void InvalidOffsetsAndPositions_Throw()
    {
        var map = new SqlLineMap("a\r\nb");

        Assert.Throws<ArgumentNullException>(() => new SqlLineMap(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetLinePosition(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetLinePosition(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetOffset(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetOffset(3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetOffset(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetOffset(1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetOffset(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlLinePosition(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlLinePosition(1, 0));
    }

    [Fact]
    public void ParseResult_ExposesMapForOriginalSource()
    {
        var result = new ScriptDomSqlParser().Parse("SELECT 1;\r\nSELECT 2;", SqlDialectVersion.Auto);

        Assert.Equal(2, result.LineMap.LineCount);
        AssertPosition(result.LineMap, result.Source.IndexOf("SELECT 2;", StringComparison.Ordinal), 2, 1);
    }

    private static void AssertPosition(SqlLineMap map, int offset, int line, int column)
    {
        var position = map.GetLinePosition(offset);
        Assert.Equal(line, position.Line);
        Assert.Equal(column, position.Column);
        Assert.Equal(offset, map.GetOffset(position));
    }
}
