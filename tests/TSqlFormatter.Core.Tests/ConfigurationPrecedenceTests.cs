using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Tests;

public sealed class ConfigurationPrecedenceTests
{
    private readonly SqlFormatterConfigurationResolver _resolver = new();

    [Fact]
    public void No_file_or_explicit_options_uses_defaults()
    {
        var result = _resolver.Resolve();

        Assert.True(result.Succeeded);
        Assert.Same(FormattingOptions.Default, result.Options);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void File_overrides_only_its_present_fields()
    {
        var result = _resolver.Resolve("""
            {"version":1,"general":{"maxLineLength":80},"keywords":{"case":"lower"}}
            """);

        Assert.True(result.Succeeded);
        Assert.Equal(80, result.Options!.General.MaxLineWidth);
        Assert.Equal(DocLineEnding.Lf, result.Options.General.LineEnding);
        Assert.Equal(4, result.Options.Indent.Size);
        Assert.Equal(KeywordCase.Lower, result.Options.Keywords.Case);
    }

    [Fact]
    public void Explicit_options_win_per_field_without_erasing_other_file_values()
    {
        var result = _resolver.Resolve("""
            {
              "version":1,
              "general":{"maxLineLength":80,"lineEnding":"crlf","finalNewLine":true},
              "indent":{"style":"tabs","size":8},
              "keywords":{"case":"lower"},
              "select":{"columns":"onePerLine"},
              "clauses":{"groupByLayout":"onePerLine","orderByLayout":"onePerLine"}
            }
            """, new FormattingOptionsOverrides(
            maxLineLength: 120,
            indentSize: 2,
            keywordCase: KeywordCase.Upper,
            orderByLayout: ClauseItemLayout.Auto));

        Assert.True(result.Succeeded);
        var options = result.Options!;
        Assert.Equal(120, options.General.MaxLineWidth);
        Assert.Equal(DocLineEnding.CrLf, options.General.LineEnding);
        Assert.True(options.General.FinalNewline);
        Assert.Equal(2, options.Indent.Size);
        Assert.True(options.Indent.UseTabs);
        Assert.Equal(KeywordCase.Upper, options.Keywords.Case);
        Assert.Equal(SelectColumnLayout.OnePerLine, options.Select.ColumnLayout);
        Assert.Equal(ClauseItemLayout.OnePerLine, options.Clauses.GroupByLayout);
        Assert.Equal(ClauseItemLayout.Auto, options.Clauses.OrderByLayout);
    }

    [Fact]
    public void Invalid_file_stops_resolution_without_applying_explicit_options()
    {
        var result = _resolver.Resolve("{" , new FormattingOptionsOverrides(maxLineLength: 120));

        Assert.False(result.Succeeded);
        Assert.Null(result.Options);
        Assert.Equal("TSF2000", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Explicit_options_can_override_defaults_without_a_file()
    {
        var result = _resolver.Resolve(explicitOptions: new FormattingOptionsOverrides(
            lineEnding: DocLineEnding.Cr, finalNewLine: true, useTabs: true));

        Assert.True(result.Succeeded);
        Assert.Equal(DocLineEnding.Cr, result.Options!.General.LineEnding);
        Assert.True(result.Options.General.FinalNewline);
        Assert.True(result.Options.Indent.UseTabs);
        Assert.Equal(100, result.Options.General.MaxLineWidth);
    }
}
