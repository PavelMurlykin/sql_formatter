using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class SelectColumnLayoutTests
{
    [Fact]
    public void One_per_line_keeps_trailing_commas()
    {
        var options = new FormattingOptions(select: new SelectOptions(SelectColumnLayout.OnePerLine));
        var result = new ScriptDomSqlFormatter().Format(
            "select Alpha,Beta,Gamma from dbo.Items", options, new FormatRequest());

        Assert.Equal("SELECT\n    Alpha,\n    Beta,\n    Gamma\nFROM dbo.Items", result.Text);
    }

    [Theory]
    [InlineData(80, "SELECT Alpha, Beta\nFROM Items")]
    [InlineData(16, "SELECT\n    Alpha,\n    Beta\nFROM Items")]
    public void Auto_wraps_at_max_line_width(int width, string expected)
    {
        var options = new FormattingOptions(general: new GeneralOptions(maxLineWidth: width));
        var result = new ScriptDomSqlFormatter().Format(
            "select Alpha,Beta from Items", options, new FormatRequest());

        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void Rejects_unknown_column_layout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SelectOptions((SelectColumnLayout)99));
    }
}
