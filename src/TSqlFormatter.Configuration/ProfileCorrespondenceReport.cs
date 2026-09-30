using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Lossless requirements-to-native correspondence. Deliberately does not decode XML enums.</summary>
public static class ProfileCorrespondenceReport
{
    public const string SnapshotSha256 = "51FC02E8E1AD6CB0487AA4B419BC543560DD2A4D93A7F73B03ED904581B66829";

    public static string Create(byte[] normalizedSnapshot, string coverageLedger)
    {
        if (normalizedSnapshot is null) throw new ArgumentNullException(nameof(normalizedSnapshot));
        using var sha = SHA256.Create();
        string hash = BitConverter.ToString(sha.ComputeHash(normalizedSnapshot)).Replace("-", "");
        if (hash != SnapshotSha256) throw new ArgumentException("The normalized profile snapshot has changed.", nameof(normalizedSnapshot));
        var snapshot = Rows(Encoding.UTF8.GetString(normalizedSnapshot));
        var ledger = Rows(coverageLedger);
        if (snapshot.Count != 977 || ledger.Count != 977 || !snapshot.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .SequenceEqual(ledger.Keys.OrderBy(k => k, StringComparer.Ordinal)))
            throw new ArgumentException("Expected matching, complete 977-path inventories.", nameof(coverageLedger));

        var output = new StringBuilder("Path\tType\tAV\tEPM\tStatus\tNativeSetting\tReadableVertical\tCompactQueries\tDecision\tEvidence\tSemantics\n");
        foreach (var p in snapshot.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var row = ledger[p.Key];
            string status = row[2];
            if (status is not ("covered" or "not_applicable")) throw new ArgumentException("Unresolved path: " + p.Key, nameof(coverageLedger));
            string mapping = row[3];
            var values = NativeFormattingPresets.Profiles.Select(profile => status == "not_applicable" ? "-" : NativeValue(profile.Options, mapping));
            output.Append(string.Join("\t", new[] { p.Key, p.Value[2], p.Value[3], p.Value[4], status, mapping }
                .Concat(values).Concat(new[] { status == "not_applicable" ? "excluded_by_user_decision" : "native_alternative_not_xml_conversion", row[4], row[5] }))).Append('\n');
        }
        return output.ToString();
    }

    private static string NativeValue(FormattingOptions options, string mapping)
    {
        string key = mapping.Split(':')[0];
        string? rootKey = options.Rules.Catalog.Definitions.Keys.OrderByDescending(k => k.Length)
            .FirstOrDefault(k => key == k || key.StartsWith(k + ".", StringComparison.Ordinal));
        var serializer = new SqlFormatterConfigurationSerializer();
        if (rootKey is not null)
        {
            var token = JObject.Parse(serializer.SerializeV2(options.With(rules: options.Rules.With(rootKey, options.Rules.Get(rootKey)))))["rules"]![rootKey]!;
            if (key != rootKey) token = token[key.Substring(rootKey.Length + 1)]
                ?? throw new ArgumentException("Unknown native member: " + key, nameof(mapping));
            return token.ToString(Formatting.None);
        }
        var root = JObject.Parse(serializer.SerializeV2(options));
        var parts = key.Split('.');
        if (parts.Length == 2 && root[parts[0]]?[parts[1]] is { } legacy) return legacy.ToString(Formatting.None);
        throw new ArgumentException("Unimplemented native correspondence: " + mapping, nameof(mapping));
    }

    private static Dictionary<string, string[]> Rows(string tsv) => tsv.TrimStart('\uFEFF').Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Skip(1).Select(l => l.Split('\t')).ToDictionary(r => r[0], StringComparer.Ordinal);
}
