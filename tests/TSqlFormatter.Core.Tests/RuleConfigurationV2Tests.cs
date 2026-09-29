using System.Text.Json;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class RuleConfigurationV2Tests
{
    private static RuleCatalog Catalog() => new(new[]
    {
        new RuleDescriptor("sample.enabled", "sample", RuleValue.FromBoolean(false)),
        new RuleDescriptor("sample.count", "sample", RuleValue.FromInteger(2), 0, 20),
        new RuleDescriptor("sample.mode", "sample", RuleValue.FromChoice("auto"),
            choices: new[] { "auto", "always", "never" }),
        new RuleDescriptor("sample.shortQuery", "sample", RuleValue.FromThreshold(
            new ThresholdRule(false, 50)), 0, 1000, dependsOn: "sample.enabled"),
        new RuleDescriptor("sample.indent", "sample", RuleValue.FromIndent(
            new IndentRule(false, 0)), -32, 32, dialect: "T-SQL"),
        new RuleDescriptor("sample.useSelectFormatting", "sample", RuleValue.FromBoolean(true))
    });

    [Fact]
    public void Version_two_round_trips_all_native_value_kinds_and_signed_indent()
    {
        var catalog = Catalog();
        var rules = new RuleOptions(catalog)
            .With("sample.enabled", RuleValue.FromBoolean(true))
            .With("sample.count", RuleValue.FromInteger(8))
            .With("sample.mode", RuleValue.FromChoice("never"))
            .With("sample.shortQuery", RuleValue.FromThreshold(new ThresholdRule(false, 60)))
            .With("sample.indent", RuleValue.FromIndent(new IndentRule(true, -1, false, "anchor", true)))
            .With("sample.useSelectFormatting", RuleValue.FromBoolean(false));
        var options = FormattingOptions.Default.With(rules: rules);
        var serializer = new SqlFormatterConfigurationSerializer(catalog);

        var json = serializer.Serialize(options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
        var loaded = serializer.Deserialize(json);

        Assert.Equal(json, serializer.Serialize(loaded));
        Assert.Equal(6, loaded.Rules.Overrides.Count);
        Assert.Equal(-1, loaded.Rules.Get("sample.indent").Indent.Offset);
        Assert.Equal("anchor", loaded.Rules.Get("sample.indent").Indent.Style);
        Assert.True(loaded.Rules.Get("sample.indent").Indent.Transparent);
        Assert.False(loaded.Rules.Get("sample.indent").Indent.OnNewLineOnly);
        Assert.False(loaded.Rules.Get("sample.shortQuery").Threshold.Enabled);
        Assert.Equal(60, loaded.Rules.Get("sample.shortQuery").Threshold.Value);
        Assert.False(loaded.Rules.Get("sample.useSelectFormatting").Boolean);
        Assert.Equal("sample", catalog.Definitions["sample.indent"].Scope);
        Assert.Equal("T-SQL", catalog.Definitions["sample.indent"].Dialect);
        Assert.Equal("sample.enabled", catalog.Definitions["sample.shortQuery"].DependsOn);
    }

    [Fact]
    public void Version_one_output_and_baseline_are_unchanged()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var v1 = serializer.Serialize(FormattingOptions.Default);
        using var document = JsonDocument.Parse(v1);
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("rules", out _));
        Assert.Equal(v1, serializer.Serialize(serializer.Deserialize(v1)));

        var rules = new RuleOptions(Catalog()).With("sample.enabled", RuleValue.FromBoolean(true));
        var baseline = FormattingOptions.Default.With(rules: rules);
        var layered = new SqlFormatterConfigurationSerializer(Catalog()).Deserialize(
            """{"version":1,"indent":{"size":2}}""");
        Assert.Equal(2, layered.Indent.Size);
        var result = new SqlFormatterConfigurationSerializer(Catalog()).Parse(
            """{"version":1,"indent":{"size":2}}""", baseline);
        Assert.True(result.Succeeded);
        Assert.True(result.Options!.Rules.Get("sample.enabled").Boolean);
        Assert.True(new FormattingOptionsOverrides(maxLineLength: 90)
            .ApplyTo(result.Options).Rules.Get("sample.enabled").Boolean);
    }

    [Fact]
    public void Version_two_overrides_only_supplied_values()
    {
        var catalog = Catalog();
        var baseline = FormattingOptions.Default.With(rules: new RuleOptions(catalog)
            .With("sample.enabled", RuleValue.FromBoolean(true)));
        var serializer = new SqlFormatterConfigurationSerializer(catalog);
        var parsed = serializer.Parse(
            """{"version":2,"rules":{"sample.mode":"always"},"indent":{"size":3}}""", baseline);

        Assert.True(parsed.Succeeded);
        Assert.Equal(3, parsed.Options!.Indent.Size);
        Assert.True(parsed.Options.Rules.Get("sample.enabled").Boolean);
        Assert.Equal("always", parsed.Options.Rules.Get("sample.mode").Choice);
        Assert.Equal(2, parsed.Options.Rules.Get("sample.count").Integer);
        Assert.True(new SqlFormatterConfigurationSerializer().Parse(
            """{"version":2,"rules":{"sample.mode":"always"}}""", baseline).Succeeded);
        Assert.Equal(2, serializer.Deserialize(serializer.SerializeV2(FormattingOptions.Default))
            .Rules.Get("sample.count").Integer);
    }

    [Theory]
    [InlineData("{\"version\":2,\"rules\":{\"sample.mode\":\"unknown\"}}")]
    [InlineData("{\"version\":2,\"rules\":{\"sample.mode\":\"\"}}")]
    [InlineData("{\"version\":2,\"rules\":{\"sample.count\":-1}}")]
    [InlineData("{\"version\":2,\"rules\":{\"sample.indent\":{\"enabled\":true,\"offset\":-1}}}")]
    [InlineData("{\"version\":2,\"rules\":{\"sample.missing\":true}}")]
    [InlineData("{\"version\":2,\"rules\":[]}")]
    [InlineData("{\"version\":1,\"rules\":{}}")]
    public void Unknown_or_invalid_values_do_not_produce_partial_options(string json)
    {
        var result = new SqlFormatterConfigurationSerializer(Catalog()).Parse(json);
        Assert.False(result.Succeeded);
        Assert.Null(result.Options);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TSF2000");
    }

    [Fact]
    public void Duplicate_rule_keys_and_catalog_dependencies_are_rejected()
    {
        var serializer = new SqlFormatterConfigurationSerializer(Catalog());
        var result = serializer.Parse(
            """{"version":2,"rules":{"sample.enabled":true,"sample.enabled":false}}""");
        Assert.False(result.Succeeded);
        Assert.Throws<ArgumentException>(() => new RuleCatalog(new[]
        {
            new RuleDescriptor("sample.child", "sample", RuleValue.FromBoolean(true),
                dependsOn: "sample.missing")
        }));
        Assert.Throws<ArgumentException>(() => new RuleCatalog(new[]
        {
            new RuleDescriptor("sample.first", "sample", RuleValue.FromBoolean(true),
                dependsOn: "sample.second"),
            new RuleDescriptor("sample.second", "sample", RuleValue.FromBoolean(true),
                dependsOn: "sample.first")
        }));
    }

    [Fact]
    public void Version_one_file_can_be_migrated_to_version_two_without_changing_base_options()
    {
        var serializer = new SqlFormatterConfigurationSerializer(Catalog());
        var original = serializer.Deserialize("""
            {"version":1,"indent":{"size":2},"keywords":{"case":"lower"}}
            """);
        var migrated = serializer.Deserialize(serializer.SerializeV2(original));

        Assert.Equal(original.Indent.Size, migrated.Indent.Size);
        Assert.Equal(original.Keywords.Case, migrated.Keywords.Case);
        Assert.Empty(migrated.Rules.Overrides);
    }
}
