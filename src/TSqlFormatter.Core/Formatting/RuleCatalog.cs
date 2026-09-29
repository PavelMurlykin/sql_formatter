using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Metadata for one native rule; XML identifiers are deliberately not used as keys.</summary>
public sealed class RuleDescriptor
{
    public RuleDescriptor(string key, string scope, RuleValue defaultValue,
        int minimum = int.MinValue, int maximum = int.MaxValue,
        IEnumerable<string>? choices = null, string? dependsOn = null,
        string? dialect = null)
    {
        if (key is null || !Regex.IsMatch(key, @"^[a-z][A-Za-z0-9]*(?:\.[a-z][A-Za-z0-9]*)+$"))
            throw new ArgumentException("Use a dotted native rule key.", nameof(key));
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope is required.", nameof(scope));
        if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum));
        Key = key;
        Scope = scope;
        DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
        Minimum = minimum;
        Maximum = maximum;
        Choices = Array.AsReadOnly((choices ?? Array.Empty<string>()).ToArray());
        DependsOn = dependsOn;
        Dialect = dialect;
        if (!Accepts(defaultValue)) throw new ArgumentException("Invalid default rule value.", nameof(defaultValue));
    }

    public string Key { get; }
    public string Scope { get; }
    public RuleValue DefaultValue { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public IReadOnlyList<string> Choices { get; }
    public string? DependsOn { get; }
    public string? Dialect { get; }

    public bool Accepts(RuleValue value)
    {
        if (value is null || value.Kind != DefaultValue.Kind) return false;
        return value.Kind switch
        {
            RuleValueKind.Integer => value.Integer >= Minimum && value.Integer <= Maximum,
            RuleValueKind.Threshold => value.Threshold.Value >= Minimum && value.Threshold.Value <= Maximum,
            RuleValueKind.Indent => value.Indent.Offset >= Minimum && value.Indent.Offset <= Maximum,
            RuleValueKind.Choice => Choices.Count == 0 || Choices.Contains(value.Choice),
            _ => true
        };
    }
}

public sealed class RuleCatalog
{
    private readonly IReadOnlyDictionary<string, RuleDescriptor> definitions;

    public RuleCatalog(IEnumerable<RuleDescriptor> descriptors)
    {
        if (descriptors is null) throw new ArgumentNullException(nameof(descriptors));
        var map = new Dictionary<string, RuleDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (descriptor is null || map.ContainsKey(descriptor.Key))
                throw new ArgumentException("Null or duplicate rule descriptor.", nameof(descriptors));
            map.Add(descriptor.Key, descriptor);
        }
        foreach (var descriptor in map.Values)
        {
            if (descriptor.DependsOn is not null && !map.ContainsKey(descriptor.DependsOn))
                throw new ArgumentException($"Unknown dependency: {descriptor.DependsOn}", nameof(descriptors));
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = descriptor;
            while (current.DependsOn is not null)
            {
                if (!visited.Add(current.Key))
                    throw new ArgumentException($"Cyclic rule dependency: {descriptor.Key}", nameof(descriptors));
                current = map[current.DependsOn];
            }
        }
        definitions = new ReadOnlyDictionary<string, RuleDescriptor>(map);
    }

    public static RuleCatalog Default { get; } = new(Array.Empty<RuleDescriptor>());
    public IReadOnlyDictionary<string, RuleDescriptor> Definitions => definitions;
    public bool TryGet(string key, out RuleDescriptor? descriptor) => definitions.TryGetValue(key, out descriptor);
}

/// <summary>Immutable overrides layered over rule defaults.</summary>
public sealed class RuleOptions
{
    private readonly IReadOnlyDictionary<string, RuleValue> overrides;

    public RuleOptions(RuleCatalog catalog, IEnumerable<KeyValuePair<string, RuleValue>>? values = null)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        var map = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        foreach (var pair in values ?? Array.Empty<KeyValuePair<string, RuleValue>>())
        {
            if (!catalog.TryGet(pair.Key, out var descriptor) || descriptor is null ||
                !descriptor.Accepts(pair.Value) || map.ContainsKey(pair.Key))
                throw new ArgumentException($"Unknown, duplicate or invalid rule: {pair.Key}", nameof(values));
            map.Add(pair.Key, pair.Value);
        }
        overrides = new ReadOnlyDictionary<string, RuleValue>(map);
    }

    public RuleCatalog Catalog { get; }
    public IReadOnlyDictionary<string, RuleValue> Overrides => overrides;

    public RuleValue Get(string key) => overrides.TryGetValue(key, out var value) ? value
        : Catalog.TryGet(key, out var descriptor) && descriptor is not null ? descriptor.DefaultValue
        : throw new KeyNotFoundException($"Unknown formatting rule: {key}");

    public RuleOptions With(string key, RuleValue value)
    {
        var changed = overrides.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        changed[key] = value;
        return new RuleOptions(Catalog, changed);
    }
}
