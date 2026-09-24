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

    [Fact]
    public void Parse_returns_options_without_diagnostics_for_valid_partial_configuration()
    {
        var result = _serializer.Parse("""
            {"version":1,"keywords":{"case":"preserve"}}
            """);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(KeywordCase.Preserve, result.Options!.Keywords.Case);
        Assert.Equal(4, result.Options.Indent.Size);
    }

    [Theory]
    [InlineData("{", "Invalid configuration JSON")]
    [InlineData("{\"version\":1,\"version\":1}", "Invalid configuration JSON")]
    [InlineData("{}", "version")]
    [InlineData("{\"version\":2}", "version")]
    [InlineData("{\"version\":999999999999999999999999}", "version")]
    [InlineData("{\"version\":1,\"future\":{}}", "future")]
    [InlineData("{\"version\":1,\"general\":null}", "general")]
    [InlineData("{\"version\":1,\"general\":{\"maxLineLength\":0}}", "general.maxLineLength")]
    [InlineData("{\"version\":1,\"general\":{\"maxLineLength\":999999999999999999999999}}", "general.maxLineLength")]
    [InlineData("{\"version\":1,\"general\":{\"lineEnding\":\"auto\"}}", "general.lineEnding")]
    [InlineData("{\"version\":1,\"general\":{\"finalNewLine\":1}}", "general.finalNewLine")]
    [InlineData("{\"version\":1,\"indent\":{\"size\":-1}}", "indent.size")]
    [InlineData("{\"version\":1,\"indent\":{\"style\":\"other\"}}", "indent.style")]
    [InlineData("{\"version\":1,\"keywords\":{\"case\":null}}", "keywords.case")]
    [InlineData("{\"version\":1,\"select\":{\"commaStyle\":\"trailing\"}}", "select.commaStyle")]
    [InlineData("{\"version\":1,\"clauses\":{\"groupByLayout\":false}}", "clauses.groupByLayout")]
    public void Parse_reports_invalid_configuration_without_options(string json, string expectedMessage)
    {
        var result = _serializer.Parse(json);

        Assert.False(result.Succeeded);
        Assert.Null(result.Options);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TSF2000", diagnostic.Code);
        Assert.Equal(FormatterDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(expectedMessage, diagnostic.Message);
        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => _serializer.Deserialize(json));
    }

    [Fact]
    public void Parse_collects_multiple_independent_errors()
    {
        var result = _serializer.Parse("""
            {
              "version": 1,
              "general": { "maxLineLength": 0, "lineEnding": "auto" },
              "select": { "columns": "wide", "commaStyle": "trailing" },
              "unknown": true
            }
            """);

        Assert.False(result.Succeeded);
        Assert.Null(result.Options);
        Assert.Equal(5, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("TSF2000", diagnostic.Code));
    }
}
