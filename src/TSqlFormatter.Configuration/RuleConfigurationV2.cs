using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

internal static class RuleConfigurationV2
{
    public static JObject Write(RuleOptions options)
    {
        var rules = new JObject();
        foreach (var entry in options.Overrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            rules[entry.Key] = entry.Value.Kind switch
            {
                RuleValueKind.Boolean => new JValue(entry.Value.Boolean),
                RuleValueKind.Integer => new JValue(entry.Value.Integer),
                RuleValueKind.Choice => new JValue(entry.Value.Choice),
                RuleValueKind.Threshold => new JObject
                {
                    ["enabled"] = entry.Value.Threshold.Enabled,
                    ["value"] = entry.Value.Threshold.Value
                },
                RuleValueKind.Indent => new JObject
                {
                    ["enabled"] = entry.Value.Indent.Enabled,
                    ["offset"] = entry.Value.Indent.Offset,
                    ["onNewLineOnly"] = entry.Value.Indent.OnNewLineOnly,
                    ["style"] = entry.Value.Indent.Style,
                    ["transparent"] = entry.Value.Indent.Transparent
                },
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            };
        }
        return rules;
    }

    public static ConfigurationParseResult Parse(JObject root, FormattingOptions baseline,
        RuleCatalog catalog)
    {
        var legacy = (JObject)root.DeepClone();
        legacy.Remove("rules");
        legacy["version"] = 1;
        var baseResult = new SqlFormatterConfigurationSerializer(catalog)
            .Parse(legacy.ToString(Formatting.None), baseline);
        var errors = baseResult.Diagnostics.ToList();
        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        if (root["rules"] is not null and not JObject)
        {
            errors.Add(Error("'rules' must be an object."));
        }
        else if (root["rules"] is JObject rules)
        {
            foreach (var property in rules.Properties())
            {
                if (!catalog.TryGet(property.Name, out var descriptor) || descriptor is null)
                {
                    errors.Add(Error($"Unknown formatting rule '{property.Name}'."));
                    continue;
                }
                var value = ReadValue(descriptor, property.Value);
                if (value is null || !descriptor.Accepts(value))
                    errors.Add(Error($"Invalid value for formatting rule '{property.Name}'."));
                else values.Add(property.Name, value);
            }
        }
        if (errors.Count > 0)
            return new ConfigurationParseResult(null, Array.AsReadOnly(errors.ToArray()));

        foreach (var pair in baseline.Rules.Overrides)
        {
            if (!values.ContainsKey(pair.Key) && catalog.TryGet(pair.Key, out var descriptor)
                && descriptor is not null && descriptor.Accepts(pair.Value))
                values.Add(pair.Key, pair.Value);
        }
        var options = baseResult.Options!.With(rules: new RuleOptions(catalog, values));
        return new ConfigurationParseResult(options, Array.Empty<FormatterDiagnostic>());
    }

    private static RuleValue? ReadValue(RuleDescriptor descriptor, JToken token)
    {
        switch (descriptor.DefaultValue.Kind)
        {
            case RuleValueKind.Boolean:
                return token.Type == JTokenType.Boolean ? RuleValue.FromBoolean(token.Value<bool>()) : null;
            case RuleValueKind.Integer:
                return TryInt(token, out var number) ? RuleValue.FromInteger(number) : null;
            case RuleValueKind.Choice:
                var choice = token.Type == JTokenType.String ? token.Value<string>() : null;
                return !string.IsNullOrWhiteSpace(choice) ? RuleValue.FromChoice(choice!) : null;
            case RuleValueKind.Threshold:
                if (token is not JObject threshold || !HasFields(threshold, "enabled", "value")
                    || threshold["enabled"]?.Type != JTokenType.Boolean
                    || !TryInt(threshold["value"], out var limit)) return null;
                return RuleValue.FromThreshold(new ThresholdRule(threshold["enabled"]!.Value<bool>(), limit));
            case RuleValueKind.Indent:
                if (token is not JObject indent || !HasFields(indent, "enabled", "offset",
                        "onNewLineOnly", "style", "transparent")
                    || indent["enabled"]?.Type != JTokenType.Boolean
                    || !TryInt(indent["offset"], out var offset)
                    || indent["onNewLineOnly"]?.Type != JTokenType.Boolean
                    || indent["style"]?.Type != JTokenType.String
                    || indent["transparent"]?.Type != JTokenType.Boolean) return null;
                try
                {
                    return RuleValue.FromIndent(new IndentRule(indent["enabled"]!.Value<bool>(), offset,
                        indent["onNewLineOnly"]!.Value<bool>(), indent["style"]!.Value<string>()!,
                        indent["transparent"]!.Value<bool>()));
                }
                catch (ArgumentOutOfRangeException) { return null; }
            default:
                return null;
        }
    }

    private static bool HasFields(JObject value, params string[] names) =>
        value.Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)
            .SequenceEqual(names.OrderBy(name => name, StringComparer.Ordinal));

    private static bool TryInt(JToken? token, out int value)
    {
        value = 0;
        return token?.Type == JTokenType.Integer && int.TryParse(token.ToString(Formatting.None),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static FormatterDiagnostic Error(string message) => new("TSF2000", message,
        FormatterDiagnosticSeverity.Error);
}
