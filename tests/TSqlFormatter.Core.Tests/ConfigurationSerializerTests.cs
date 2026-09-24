using System.Text.Json;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Tests;

public sealed class ConfigurationSerializerTests
{
    private readonly SqlFormatterConfigurationSerializer _serializer = new();

    [Fact]
    public void Serializes_versioned_defaults_and_round_trips()
    {
        var json = _serializer.Serialize(FormattingOptions.Default);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(".tsqlformatter.json", SqlFormatterConfigurationSerializer.FileName);
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(100, root.GetProperty("general").GetProperty("maxLineLength").GetInt32());
        Assert.Equal("lf", root.GetProperty("general").GetProperty("lineEnding").GetString());
        Assert.False(root.GetProperty("general").GetProperty("finalNewLine").GetBoolean());
        Assert.Equal("spaces", root.GetProperty("indent").GetProperty("style").GetString());
        Assert.Equal("upper", root.GetProperty("keywords").GetProperty("case").GetString());
        Assert.EndsWith("\n", json);
        Assert.DoesNotContain("\r", json);
        Assert.Equal(json, _serializer.Serialize(_serializer.Deserialize(json)));
    }

    [Fact]
    public void Deserializes_partial_configuration_using_defaults()
    {
        var options = _serializer.Deserialize("""
            {"version":1,"keywords":{"case":"lower"}}
            """);

        Assert.Equal(KeywordCase.Lower, options.Keywords.Case);
        Assert.Equal(100, options.General.MaxLineWidth);
        Assert.Equal(4, options.Indent.Size);
        Assert.Equal(SelectColumnLayout.Auto, options.Select.ColumnLayout);
    }

    [Fact]
    public void Deserialized_options_affect_formatter()
    {
        var options = _serializer.Deserialize("""
            {
              "version": 1,
              "general": { "maxLineLength": 120, "lineEnding": "crlf", "finalNewLine": true },
              "indent": { "style": "spaces", "size": 2 },
              "keywords": { "case": "lower" },
              "select": { "columns": "onePerLine" },
              "clauses": { "groupByLayout": "onePerLine", "orderByLayout": "auto" }
            }
            """);
        var result = new ScriptDomSqlFormatter().Format("SELECT A,B FROM T", options,
            new FormatRequest());

        Assert.Equal("select\r\n  A,\r\n  B\r\nfrom T\r\n", result.Text);
        Assert.Equal(120, options.General.MaxLineWidth);
        Assert.Equal(DocLineEnding.CrLf, options.General.LineEnding);
        Assert.True(options.General.FinalNewline);
        Assert.Equal(ClauseItemLayout.OnePerLine, options.Clauses.GroupByLayout);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":1,\"keywords\":{\"case\":\"mixed\"}}")]
    public void Rejects_invalid_version_or_value(string json)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => _serializer.Deserialize(json));
    }
}
