using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Configuration;

/// <summary>Reads version 1 and 2 .tsqlformatter.json settings without changing version-1 output.</summary>
public sealed class SqlFormatterConfigurationSerializer
{
    public const string FileName = ".tsqlformatter.json";

    private readonly RuleCatalog ruleCatalog;

    public SqlFormatterConfigurationSerializer(RuleCatalog? ruleCatalog = null)
    {
        this.ruleCatalog = ruleCatalog ?? RuleCatalog.Default;
    }

    public string Serialize(FormattingOptions options) => SerializeCore(options,
        options?.Rules.Overrides.Count > 0);

    public string SerializeV2(FormattingOptions options) => SerializeCore(options, true);

    private static string SerializeCore(FormattingOptions options, bool version2)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var root = new JObject
        {
            ["version"] = version2 ? 2 : 1,
            ["general"] = new JObject
            {
                ["maxLineLength"] = options.General.MaxLineWidth,
                ["lineEnding"] = FormatLineEnding(options.General.LineEnding),
                ["finalNewLine"] = options.General.FinalNewline
            },
            ["indent"] = new JObject
            {
                ["style"] = options.Indent.UseTabs ? "tabs" : "spaces",
                ["size"] = options.Indent.Size
            },
            ["keywords"] = new JObject
            {
                ["case"] = FormatKeywordCase(options.Keywords.Case)
            },
            ["select"] = new JObject
            {
                ["columns"] = FormatLayout(options.Select.ColumnLayout)
            },
            ["joins"] = new JObject
            {
                ["clauseNewLine"] = options.Joins.ClauseNewLine,
                ["conditionNewLine"] = options.Joins.ConditionNewLine
            },
            ["where"] = new JObject
            {
                ["conditionNewLine"] = options.Where.ConditionNewLine,
                ["booleanOperatorNewLine"] = options.Where.BooleanOperatorNewLine
            },
            ["clauses"] = new JObject
            {
                ["groupByLayout"] = FormatLayout(options.Clauses.GroupByLayout),
                ["orderByLayout"] = FormatLayout(options.Clauses.OrderByLayout)
            },
            ["alignment"] = new JObject
            {
                ["selectAliases"] = options.Alignment.SelectAliases,
                ["setAssignments"] = options.Alignment.SetAssignments,
                ["declareTypes"] = options.Alignment.DeclareTypes
            }
        };

        if (version2) root["rules"] = RuleConfigurationV2.Write(options.Rules);

        var builder = new StringBuilder();
        using (var textWriter = new StringWriter(builder, CultureInfo.InvariantCulture) { NewLine = "\n" })
        using (var writer = new JsonTextWriter(textWriter)
        {
            Formatting = Formatting.Indented,
            Indentation = 2,
            IndentChar = ' '
        })
        {
            root.WriteTo(writer);
        }

        return builder.ToString() + "\n";
    }

    public FormattingOptions Deserialize(string json)
    {
        var result = Parse(json);
        if (!result.Succeeded)
        {
            throw new JsonSerializationException(result.Diagnostics[0].Message);
        }

        return result.Options!;
    }

    /// <summary>Validates a configuration and returns TSF2000 diagnostics without applying partial settings.</summary>
    public ConfigurationParseResult Parse(string json, FormattingOptions? baseline = null)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));

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

        if (root["version"]?.Type == JTokenType.Integer &&
            int.TryParse(root["version"]!.ToString(Formatting.None), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var formatVersion) && formatVersion == 2)
            return RuleConfigurationV2.Parse(root, baseline ?? FormattingOptions.Default,
                ReferenceEquals(ruleCatalog, RuleCatalog.Default) && baseline is not null
                    ? baseline.Rules.Catalog : ruleCatalog);

        var diagnostics = new List<FormatterDiagnostic>();
        ValidateRoot(root, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new ConfigurationParseResult(null, Array.AsReadOnly(diagnostics.ToArray()));
        }

        return new ConfigurationParseResult(DeserializeValid(root, baseline ?? FormattingOptions.Default),
            Array.Empty<FormatterDiagnostic>());
    }

    private static FormattingOptions DeserializeValid(JObject root, FormattingOptions baseline)
    {
        var general = GetSection(root, "general");
        var indent = GetSection(root, "indent");
        var keywords = GetSection(root, "keywords");
        var select = GetSection(root, "select");
        var joins = GetSection(root, "joins");
        var where = GetSection(root, "where");
        var clauses = GetSection(root, "clauses");
        var alignment = GetSection(root, "alignment");

        return new FormattingOptions(
            general: new GeneralOptions(
                GetInt32(general, "maxLineLength", baseline.General.MaxLineWidth),
                ParseLineEnding(GetString(general, "lineEnding", FormatLineEnding(baseline.General.LineEnding))),
                GetBoolean(general, "finalNewLine", baseline.General.FinalNewline)),
            indent: new IndentOptions(
                GetInt32(indent, "size", baseline.Indent.Size),
                ParseIndentStyle(GetString(indent, "style", baseline.Indent.UseTabs ? "tabs" : "spaces"))),
            keywords: new KeywordOptions(
                ParseKeywordCase(GetString(keywords, "case", FormatKeywordCase(baseline.Keywords.Case)))),
            select: new SelectOptions(
                ParseSelectLayout(GetString(select, "columns", FormatLayout(baseline.Select.ColumnLayout)))),
            joins: new JoinOptions(
                GetBoolean(joins, "clauseNewLine", baseline.Joins.ClauseNewLine),
                GetBoolean(joins, "conditionNewLine", baseline.Joins.ConditionNewLine)),
            where: new WhereOptions(
                GetBoolean(where, "conditionNewLine", baseline.Where.ConditionNewLine),
                GetBoolean(where, "booleanOperatorNewLine", baseline.Where.BooleanOperatorNewLine)),
            clauses: new QueryClauseOptions(
                ParseClauseLayout(GetString(clauses, "groupByLayout", FormatLayout(baseline.Clauses.GroupByLayout))),
                ParseClauseLayout(GetString(clauses, "orderByLayout", FormatLayout(baseline.Clauses.OrderByLayout)))),
            alignment: new AlignmentOptions(
                GetBoolean(alignment, "selectAliases", baseline.Alignment.SelectAliases),
                GetBoolean(alignment, "setAssignments", baseline.Alignment.SetAssignments),
                GetBoolean(alignment, "declareTypes", baseline.Alignment.DeclareTypes)),
            rules: baseline.Rules);
    }

    private static ConfigurationParseResult Failed(string message) => new(
        null,
        Array.AsReadOnly(new[] { Diagnostic(message) }));

    private static FormatterDiagnostic Diagnostic(string message) => new(
        "TSF2000", message, FormatterDiagnosticSeverity.Error);

    private static void ValidateRoot(JObject root, List<FormatterDiagnostic> diagnostics)
    {
        foreach (var property in root.Properties())
        {
            switch (property.Name)
            {
                case "version":
                case "general":
                case "indent":
                case "keywords":
                case "select":
                case "joins":
                case "where":
                case "clauses":
                case "alignment":
                    break;
                default:
                    diagnostics.Add(Diagnostic($"Unknown configuration property '{property.Name}'."));
                    break;
            }
        }

        var version = root["version"];
        if (version is null || version.Type != JTokenType.Integer ||
            !int.TryParse(version.ToString(Formatting.None), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var versionNumber) || versionNumber != 1)
        {
            diagnostics.Add(Diagnostic("'version' must be the integer 1."));
        }

        ValidateSection(root, "general", diagnostics);
        ValidateSection(root, "indent", diagnostics);
        ValidateSection(root, "keywords", diagnostics);
        ValidateSection(root, "select", diagnostics);
        ValidateSection(root, "joins", diagnostics);
        ValidateSection(root, "where", diagnostics);
        ValidateSection(root, "clauses", diagnostics);
        ValidateSection(root, "alignment", diagnostics);
    }

    private static void ValidateSection(JObject root, string name, List<FormatterDiagnostic> diagnostics)
    {
        var section = root[name];
        if (section is null) return;
        if (section is not JObject properties)
        {
            diagnostics.Add(Diagnostic($"'{name}' must be an object."));
            return;
        }

        foreach (var property in properties.Properties())
        {
            var path = $"{name}.{property.Name}";
            switch (path)
            {
                case "general.maxLineLength":
                    ValidateInteger(property.Value, path, 1, diagnostics);
                    break;
                case "general.lineEnding":
                    ValidateChoice(property.Value, path, diagnostics, "lf", "crlf", "cr");
                    break;
                case "general.finalNewLine":
                case "joins.clauseNewLine":
                case "joins.conditionNewLine":
                case "where.conditionNewLine":
                case "where.booleanOperatorNewLine":
                case "alignment.selectAliases":
                case "alignment.setAssignments":
                case "alignment.declareTypes":
                    if (property.Value.Type != JTokenType.Boolean)
                        diagnostics.Add(Diagnostic($"'{path}' must be a boolean."));
                    break;
                case "indent.style":
                    ValidateChoice(property.Value, path, diagnostics, "spaces", "tabs");
                    break;
                case "indent.size":
                    ValidateInteger(property.Value, path, 0, diagnostics);
                    break;
                case "keywords.case":
                    ValidateChoice(property.Value, path, diagnostics, "upper", "lower", "preserve");
                    break;
                case "select.columns":
                case "clauses.groupByLayout":
                case "clauses.orderByLayout":
                    ValidateChoice(property.Value, path, diagnostics, "auto", "onePerLine");
                    break;
                default:
                    diagnostics.Add(Diagnostic($"Unknown configuration property '{path}'."));
                    break;
            }
        }
    }

    private static void ValidateInteger(JToken value, string path, int minimum,
        List<FormatterDiagnostic> diagnostics)
    {
        if (value.Type != JTokenType.Integer ||
            !int.TryParse(value.ToString(Formatting.None), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var number) || number < minimum)
        {
            diagnostics.Add(Diagnostic($"'{path}' must be an integer of at least {minimum}."));
        }
    }

    private static void ValidateChoice(JToken value, string path,
        List<FormatterDiagnostic> diagnostics, params string[] choices)
    {
        if (value.Type != JTokenType.String ||
            !choices.Contains(value.Value<string>(), StringComparer.Ordinal))
        {
            diagnostics.Add(Diagnostic($"'{path}' must be one of: {string.Join(", ", choices)}."));
        }
    }

    private static JObject? GetSection(JObject root, string name)
    {
        var value = root[name];
        if (value is null) return null;
        return value as JObject
            ?? throw new JsonSerializationException($"'{name}' must be an object.");
    }

    private static int GetInt32(JObject? section, string name, int fallback)
    {
        var value = section?[name];
        if (value is null) return fallback;
        return value.Type == JTokenType.Integer
            ? value.Value<int>()
            : throw new JsonSerializationException($"'{name}' must be an integer.");
    }

    private static bool GetBoolean(JObject? section, string name, bool fallback)
    {
        var value = section?[name];
        if (value is null) return fallback;
        return value.Type == JTokenType.Boolean
            ? value.Value<bool>()
            : throw new JsonSerializationException($"'{name}' must be a boolean.");
    }

    private static string GetString(JObject? section, string name, string fallback)
    {
        var value = section?[name];
        if (value is null) return fallback;
        return value.Type == JTokenType.String
            ? value.Value<string>()!
            : throw new JsonSerializationException($"'{name}' must be a string.");
    }

    private static DocLineEnding ParseLineEnding(string value) => value switch
    {
        "lf" => DocLineEnding.Lf,
        "crlf" => DocLineEnding.CrLf,
        "cr" => DocLineEnding.Cr,
        _ => throw new JsonSerializationException($"Unsupported line ending '{value}'.")
    };

    private static bool ParseIndentStyle(string value) => value switch
    {
        "spaces" => false,
        "tabs" => true,
        _ => throw new JsonSerializationException($"Unsupported indent style '{value}'.")
    };

    private static KeywordCase ParseKeywordCase(string value) => value switch
    {
        "upper" => KeywordCase.Upper,
        "lower" => KeywordCase.Lower,
        "preserve" => KeywordCase.Preserve,
        _ => throw new JsonSerializationException($"Unsupported keyword case '{value}'.")
    };

    private static SelectColumnLayout ParseSelectLayout(string value) => value switch
    {
        "auto" => SelectColumnLayout.Auto,
        "onePerLine" => SelectColumnLayout.OnePerLine,
        _ => throw new JsonSerializationException($"Unsupported select column layout '{value}'.")
    };

    private static ClauseItemLayout ParseClauseLayout(string value) => value switch
    {
        "auto" => ClauseItemLayout.Auto,
        "onePerLine" => ClauseItemLayout.OnePerLine,
        _ => throw new JsonSerializationException($"Unsupported clause item layout '{value}'.")
    };

    private static string FormatLineEnding(DocLineEnding value) => value switch
    {
        DocLineEnding.Lf => "lf",
        DocLineEnding.CrLf => "crlf",
        DocLineEnding.Cr => "cr",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string FormatKeywordCase(KeywordCase value) => value switch
    {
        KeywordCase.Upper => "upper",
        KeywordCase.Lower => "lower",
        KeywordCase.Preserve => "preserve",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string FormatLayout(SelectColumnLayout value) => value switch
    {
        SelectColumnLayout.Auto => "auto",
        SelectColumnLayout.OnePerLine => "onePerLine",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string FormatLayout(ClauseItemLayout value) => value switch
    {
        ClauseItemLayout.Auto => "auto",
        ClauseItemLayout.OnePerLine => "onePerLine",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
