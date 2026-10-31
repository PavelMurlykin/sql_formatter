using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TSqlFormatter.Core.Analysis;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>
/// Reads the optional <c>analysis</c> section of .tsqlformatter.json:
/// <c>{ "analysis": { "disabled": ["TSQL003"], "severity": { "TSQL004": "warning" } } }</c>.
/// The section is validated independently of the formatting sections.
/// </summary>
public sealed class AnalysisConfigurationParser
{
    public AnalysisConfigurationParseResult Parse(string json, AnalysisConfiguration? defaults = null)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));

        defaults ??= AnalysisConfiguration.Default;
        JObject root;
        try
        {
            root = JObject.Parse(json, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
        }
        catch (JsonException exception)
        {
            return Failed($"Invalid configuration JSON: {exception.Message}");
        }

        var section = root["analysis"];
        if (section is null)
        {
            return new AnalysisConfigurationParseResult(
                defaults, Array.Empty<FormatterDiagnostic>());
        }

        if (section is not JObject properties)
        {
            return Failed("'analysis' must be an object.");
        }

        var diagnostics = new List<FormatterDiagnostic>();
        var enabled = defaults.Enabled;
        var disabled = defaults.DisabledRules.ToList();
        var severities = new Dictionary<string, AnalysisSeverity>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in defaults.SeverityOverrides) severities[pair.Key] = pair.Value;
        foreach (var property in properties.Properties())
        {
            if (property.Name == "enabled")
            {
                if (property.Value.Type != JTokenType.Boolean)
                    diagnostics.Add(Diagnostic("'analysis.enabled' must be a boolean."));
                else enabled = property.Value.Value<bool>();
            }
            else if (property.Name == "disabled")
            {
                if (property.Value is not JArray entries)
                {
                    diagnostics.Add(Diagnostic("'analysis.disabled' must be an array of rule codes."));
                    continue;
                }

                disabled.Clear();
                foreach (var entry in entries)
                {
                    var code = entry.Type == JTokenType.String ? entry.Value<string>() : null;
                    if (code is null || code.Trim().Length == 0)
                    {
                        diagnostics.Add(Diagnostic("'analysis.disabled' must contain non-empty rule codes."));
                    }
                    else
                    {
                        disabled.Add(code.Trim());
                    }
                }
            }
            else if (property.Name == "severity")
            {
                if (property.Value is not JObject overrides)
                {
                    diagnostics.Add(Diagnostic(
                        "'analysis.severity' must be an object of rule code to severity."));
                    continue;
                }

                foreach (var pair in overrides.Properties())
                {
                    string? value = null;
                    if (pair.Value is { } token && token.Type == JTokenType.String)
                    {
                        value = token.Value<string>();
                    }

                    if (TryParseSeverity(value, out var severity))
                    {
                        severities[pair.Name.Trim()] = severity;
                    }
                    else
                    {
                        diagnostics.Add(Diagnostic(
                            $"'analysis.severity.{pair.Name}' must be one of: info, warning, error."));
                    }
                }
            }
            else
            {
                diagnostics.Add(Diagnostic($"Unknown configuration property 'analysis.{property.Name}'."));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new AnalysisConfigurationParseResult(null, Array.AsReadOnly(diagnostics.ToArray()));
        }

        return new AnalysisConfigurationParseResult(
            new AnalysisConfiguration(disabled, severities, enabled), Array.Empty<FormatterDiagnostic>());
    }

    private static bool TryParseSeverity(string? value, out AnalysisSeverity severity)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "info":
                severity = AnalysisSeverity.Info;
                return true;
            case "warning":
                severity = AnalysisSeverity.Warning;
                return true;
            case "error":
                severity = AnalysisSeverity.Error;
                return true;
            default:
                severity = AnalysisSeverity.Info;
                return false;
        }
    }

    private static AnalysisConfigurationParseResult Failed(string message) =>
        new(null, Array.AsReadOnly(new[] { Diagnostic(message) }));

    private static FormatterDiagnostic Diagnostic(string message) =>
        new("TSF2000", message, FormatterDiagnosticSeverity.Error);
}
