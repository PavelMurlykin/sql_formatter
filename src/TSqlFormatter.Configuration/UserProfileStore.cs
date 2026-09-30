using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Native per-user named snapshots; duplicate replacement requires explicit confirmation by the UI.</summary>
public sealed class UserProfileStore
{
    private readonly SortedDictionary<string, FormattingOptions> profiles = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Names => Array.AsReadOnly(profiles.Keys.ToArray());
    public bool Contains(string name) => profiles.ContainsKey(name);
    public FormattingOptions Get(string name) => profiles.TryGetValue(name, out var value) ? value
        : throw new KeyNotFoundException("Unknown user profile: " + name);

    public void Save(string name, FormattingOptions options, bool replace = false)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 100) throw new ArgumentException("Use a profile name of 1–100 characters.", nameof(name));
        if (profiles.ContainsKey(name) && !replace) throw new ArgumentException("Profile already exists: " + name, nameof(name));
        profiles[name] = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Serialize()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        return new JArray(profiles.Select(p => new JObject
        {
            ["name"] = p.Key, ["configuration"] = JObject.Parse(serializer.SerializeV2(p.Value))
        })).ToString(Formatting.Indented);
    }

    public static UserProfileStore Deserialize(string json)
    {
        var store = new UserProfileStore();
        if (string.IsNullOrWhiteSpace(json)) return store;
        var serializer = new SqlFormatterConfigurationSerializer();
        foreach (var entry in JArray.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }))
        {
            if (entry is not JObject o || o.Properties().Count() != 2 || o["name"]?.Type != JTokenType.String
                || o["configuration"] is not JObject configuration)
                throw new JsonSerializationException("Invalid user profile store.");
            store.Save(o["name"]!.Value<string>()!, serializer.Deserialize(configuration.ToString()));
        }
        return store;
    }
}
