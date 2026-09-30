using Newtonsoft.Json.Linq;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

public enum SettingsFieldKind { Boolean, Integer, Choice }

/// <summary>A scalar editor field, including every member of compound native rules.</summary>
public sealed class SettingsField
{
    internal SettingsField(string id, string category, string scope, SettingsFieldKind kind,
        int minimum = 0, int maximum = int.MaxValue, IEnumerable<string>? choices = null,
        string? ruleKey = null, string? member = null, string? dependency = null, string? dialect = null)
    {
        Id = id;
        Category = category;
        Scope = scope;
        Kind = kind;
        Minimum = minimum;
        Maximum = maximum;
        Choices = Array.AsReadOnly((choices ?? Array.Empty<string>()).ToArray());
        RuleKey = ruleKey;
        Member = member;
        Dependency = dependency;
        Dialect = dialect;
    }

    public string Id { get; }
    public string Category { get; }
    public string Scope { get; }
    public SettingsFieldKind Kind { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public IReadOnlyList<string> Choices { get; }
    public string? RuleKey { get; }
    public string? Member { get; }
    public string? Dependency { get; }
    public string? Dialect { get; }
    public override string ToString() => Id;
}

/// <summary>Shared, transactional settings model for both IDE adapters. No SQL or IDE services.</summary>
public sealed class SettingsEditorModel
{
    private readonly SqlFormatterConfigurationSerializer serializer;
    private readonly JObject defaults;
    private readonly Dictionary<string, SettingsField> fields;

    public SettingsEditorModel(FormattingOptions? options = null)
    {
        Options = options ?? FormattingOptions.Default;
        serializer = new SqlFormatterConfigurationSerializer(Options.Rules.Catalog);
        defaults = JObject.Parse(serializer.SerializeV2(FormattingOptions.Default));
        defaults["rules"] = RuleConfigurationV2.Write(new RuleOptions(Options.Rules.Catalog,
            Options.Rules.Catalog.Definitions.Select(p => new KeyValuePair<string, RuleValue>(p.Key, p.Value.DefaultValue))));
        var list = LegacyFields().Concat(Options.Rules.Catalog.Definitions.Values
            .OrderBy(d => d.Key, StringComparer.Ordinal).SelectMany(NativeFields)).ToArray();
        Fields = Array.AsReadOnly(list);
        fields = list.ToDictionary(f => f.Id, StringComparer.Ordinal);
    }

    public FormattingOptions Options { get; private set; }
    public IReadOnlyList<SettingsField> Fields { get; }

    public IEnumerable<SettingsField> Find(string? search = null, string? category = null) => Fields.Where(f =>
        (string.IsNullOrEmpty(category) || f.Category == category) &&
        (string.IsNullOrWhiteSpace(search) || (f.Id + " " + f.Scope).IndexOf(search!.Trim(), StringComparison.OrdinalIgnoreCase) >= 0));

    public object Get(string id)
    {
        var field = GetField(id);
        var value = Token(Document(includeDefaults: true), field)!;
        return field.Kind switch
        {
            SettingsFieldKind.Boolean => value.Value<bool>(),
            SettingsFieldKind.Integer => value.Value<int>(),
            _ => value.Value<string>()!
        };
    }

    public bool IsOverridden(string id) => GetField(id).RuleKey is { } key
        ? Options.Rules.Overrides.ContainsKey(key) : !JToken.DeepEquals(Token(Document(true), GetField(id)), Token(defaults, GetField(id)));

    public string Explain(string id)
    {
        var f = GetField(id);
        var text = f.Scope + "; " + (IsOverridden(id) ? "explicit value" : "default/inherited value");
        if (f.Dependency is { } dependency)
            text += "; depends on " + dependency + " = " + Get("rules." + dependency);
        if (f.RuleKey is { } key && f.Member is not null && f.Member != "enabled")
            text += "; " + key + ".enabled = " + Get("rules." + key + ".enabled");
        if (f.RuleKey?.StartsWith("subquery.", StringComparison.Ordinal) == true && f.RuleKey != "subquery.useSelectFormatting")
            text += "; local layout requires subquery.useSelectFormatting = false";
        if (f.RuleKey?.StartsWith("merge.update.set.", StringComparison.Ordinal) == true)
            text += "; local layout requires merge.update.useStatementFormatting = false";
        if (f.RuleKey?.StartsWith("merge.insert.", StringComparison.Ordinal) == true && f.RuleKey != "merge.insert.useStatementFormatting")
            text += "; local layout requires merge.insert.useStatementFormatting = false";
        if (f.Dialect is not null) text += "; dialect: " + f.Dialect;
        return text + ". Disabled/dependent values are retained for later use.";
    }

    public void Set(string id, object value)
    {
        var f = GetField(id);
        bool valid = f.Kind switch
        {
            SettingsFieldKind.Boolean => value is bool,
            SettingsFieldKind.Integer => value is int n && n >= f.Minimum && n <= f.Maximum,
            _ => value is string s && f.Choices.Contains(s)
        };
        if (!valid) throw new ArgumentException("Invalid value for " + id, nameof(value));
        var document = Document(includeDefaults: false);
        if (f.RuleKey is { } key && document["rules"]![key] is null)
            document["rules"]![key] = defaults["rules"]![key]!.DeepClone();
        Replace(document, f, JToken.FromObject(value));
        if (f.RuleKey is { } nativeKey && JToken.DeepEquals(document["rules"]![nativeKey], defaults["rules"]![nativeKey]))
            ((JObject)document["rules"]!).Remove(nativeKey);
        // Validation is atomic: the previous options remain usable after a rejected edit/import.
        Options = serializer.Deserialize(document.ToString());
    }

    public void Reset(string id)
    {
        var f = GetField(id);
        var token = Token(defaults, f)!;
        Set(id, f.Kind switch
        {
            SettingsFieldKind.Boolean => (object)token.Value<bool>(),
            SettingsFieldKind.Integer => token.Value<int>(),
            _ => token.Value<string>()!
        });
    }

    public void ResetAll() => Options = FormattingOptions.Default.With(rules: new RuleOptions(Options.Rules.Catalog));
    public string Export() => serializer.SerializeV2(Options);
    public void Import(string json) => Options = serializer.Deserialize(json);

    private SettingsField GetField(string id) => fields.TryGetValue(id, out var field) ? field
        : throw new KeyNotFoundException("Unknown setting: " + id);

    private JObject Document(bool includeDefaults)
    {
        var document = JObject.Parse(Export());
        if (includeDefaults)
            foreach (var p in ((JObject)defaults["rules"]!).Properties())
                if (document["rules"]![p.Name] is null) document["rules"]![p.Name] = p.Value.DeepClone();
        return document;
    }

    private static JToken? Token(JObject document, SettingsField f)
    {
        if (f.RuleKey is { } key) return f.Member is null ? document["rules"]![key] : document["rules"]![key]![f.Member];
        var parts = f.Id.Split('.');
        return document[parts[0]]![parts[1]];
    }

    private static void Replace(JObject document, SettingsField f, JToken value)
    {
        if (f.RuleKey is { } key)
        {
            if (f.Member is null) document["rules"]![key] = value;
            else document["rules"]![key]![f.Member] = value;
        }
        else
        {
            var parts = f.Id.Split('.');
            document[parts[0]]![parts[1]] = value;
        }
    }

    private static IEnumerable<SettingsField> LegacyFields()
    {
        yield return new("general.maxLineLength", "general", "Preferred line width", SettingsFieldKind.Integer, 1);
        yield return LegacyChoice("general.lineEnding", "lf", "crlf", "cr");
        yield return LegacyChoice("indent.style", "spaces", "tabs");
        yield return new("indent.size", "indent", "Indent width", SettingsFieldKind.Integer);
        yield return LegacyChoice("keywords.case", "upper", "lower", "preserve");
        foreach (var key in new[] { "select.columns", "clauses.groupByLayout", "clauses.orderByLayout" })
            yield return LegacyChoice(key, "auto", "onePerLine");
        foreach (var key in new[] { "general.finalNewLine", "joins.clauseNewLine", "joins.conditionNewLine",
                     "where.conditionNewLine", "where.booleanOperatorNewLine", "alignment.selectAliases",
                     "alignment.setAssignments", "alignment.declareTypes" })
            yield return new(key, key.Split('.')[0], "Version 1 baseline", SettingsFieldKind.Boolean);
    }

    private static SettingsField LegacyChoice(string id, params string[] values) =>
        new(id, id.Split('.')[0], "Version 1 baseline", SettingsFieldKind.Choice, choices: values);

    private static IEnumerable<SettingsField> NativeFields(RuleDescriptor d)
    {
        string category = d.Key.Split('.')[0];
        SettingsField Field(string? member, SettingsFieldKind kind, IEnumerable<string>? choices = null) =>
            new("rules." + d.Key + (member is null ? "" : "." + member), category, d.Scope, kind,
                d.Minimum, d.Maximum, choices, d.Key, member, d.DependsOn, d.Dialect);
        switch (d.DefaultValue.Kind)
        {
            case RuleValueKind.Boolean: yield return Field(null, SettingsFieldKind.Boolean); break;
            case RuleValueKind.Integer: yield return Field(null, SettingsFieldKind.Integer); break;
            case RuleValueKind.Choice: yield return Field(null, SettingsFieldKind.Choice, d.Choices); break;
            case RuleValueKind.Threshold:
                yield return Field("enabled", SettingsFieldKind.Boolean);
                yield return Field("value", SettingsFieldKind.Integer);
                break;
            case RuleValueKind.Indent:
                yield return Field("enabled", SettingsFieldKind.Boolean);
                yield return Field("offset", SettingsFieldKind.Integer);
                yield return Field("onNewLineOnly", SettingsFieldKind.Boolean);
                yield return Field("style", SettingsFieldKind.Choice, new[] { "relative", "absolute", "anchor" });
                yield return Field("transparent", SettingsFieldKind.Boolean);
                break;
        }
    }
}
