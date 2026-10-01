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
    public string? DefaultProfileId { get; private set; }

    public IReadOnlyList<FormattingProfile> AvailableProfiles => Array.AsReadOnly(
        new FormattingProfileCatalog().Profiles.Select(p => new FormattingProfile("builtin:" + p.Id, p.Name, p.Options))
            .Concat(NativeFormattingPresets.Profiles.Select(p => new FormattingProfile("native:" + p.Id, p.Name, p.Options)))
            .Concat(profiles.Select(p => new FormattingProfile("user:" + p.Key, p.Key, p.Value))).ToArray());

    public FormattingOptions ResolveDefault(FormattingOptions fallback) => DefaultProfileId is null ? fallback :
        AvailableProfiles.Single(p => string.Equals(p.Id, DefaultProfileId, StringComparison.OrdinalIgnoreCase)).Options;

    public void SetDefault(string? id)
    {
        if (id is null) { DefaultProfileId = null; return; }
        var profile = AvailableProfiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (profile is null) throw new ArgumentException("Unknown default profile: " + id, nameof(id));
        DefaultProfileId = profile.Id;
    }
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
        var entries = new JArray(profiles.Select(p => new JObject
        {
            ["name"] = p.Key, ["configuration"] = JObject.Parse(serializer.SerializeV2(p.Value))
        }));
        // Keep the earlier array representation when no default has been selected.
        return (DefaultProfileId is null ? (JToken)entries : new JObject
        { ["version"] = 1, ["profiles"] = entries, ["defaultProfile"] = DefaultProfileId }).ToString(Formatting.Indented);
    }

    public static UserProfileStore Deserialize(string json)
    {
        var store = new UserProfileStore();
        if (string.IsNullOrWhiteSpace(json)) return store;
        var serializer = new SqlFormatterConfigurationSerializer();
        var document = JToken.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        string? defaultId = null;
        JArray entries;
        if (document is JArray array) entries = array;
        else if (document is JObject envelope && envelope.Properties().Count() == 3
                 && envelope["version"]?.Type == JTokenType.Integer && envelope["version"]!.Value<int>() == 1
                 && envelope["profiles"] is JArray list && envelope["defaultProfile"]?.Type == JTokenType.String)
        { entries = list; defaultId = envelope["defaultProfile"]!.Value<string>(); }
        else throw new JsonSerializationException("Invalid user profile store.");
        foreach (var entry in entries)
        {
            if (entry is not JObject o || o.Properties().Count() != 2 || o["name"]?.Type != JTokenType.String
                || o["configuration"] is not JObject configuration)
                throw new JsonSerializationException("Invalid user profile store.");
            store.Save(o["name"]!.Value<string>()!, serializer.Deserialize(configuration.ToString()));
        }
        store.SetDefault(defaultId);
        return store;
    }
}
