using Newtonsoft.Json.Linq;

namespace TSqlFormatter.Configuration;

public enum SqlEnvironmentKind { Unknown, Dev, Test, Prod }

/// <summary>Only the active document's names; no connection string or credentials.</summary>
public sealed class SqlEnvironmentContext
{
    public SqlEnvironmentContext(string documentMoniker, string? server = null, string? database = null)
    { DocumentMoniker = documentMoniker ?? throw new ArgumentNullException(nameof(documentMoniker)); Server = server; Database = database; }
    public string DocumentMoniker { get; }
    public string? Server { get; }
    public string? Database { get; }
    public bool SameAs(SqlEnvironmentContext other) =>
        string.Equals(DocumentMoniker, other.DocumentMoniker, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Database, other.Database, StringComparison.OrdinalIgnoreCase);
}

public sealed class SqlEnvironmentRule
{
    public SqlEnvironmentRule(SqlEnvironmentKind kind, string serverContains = "", string databaseContains = "",
        string documentContains = "", string? color = null)
    {
        if (kind is not (SqlEnvironmentKind.Dev or SqlEnvironmentKind.Test or SqlEnvironmentKind.Prod))
            throw new ArgumentException("Environment must be DEV, TEST or PROD.", nameof(kind));
        Kind = kind; ServerContains = Pattern(serverContains); DatabaseContains = Pattern(databaseContains);
        DocumentContains = Pattern(documentContains);
        if (ServerContains.Length + DatabaseContains.Length + DocumentContains.Length == 0)
            throw new ArgumentException("An environment rule needs at least one nonempty match field.");
        color ??= DefaultColor(kind);
        if (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit))
            throw new ArgumentException("Environment color must be #RRGGBB.", nameof(color));
        Color = color.ToUpperInvariant();
    }
    public SqlEnvironmentKind Kind { get; }
    public string Label => Kind.ToString().ToUpperInvariant();
    public string ServerContains { get; }
    public string DatabaseContains { get; }
    public string DocumentContains { get; }
    public string Color { get; }
    public static string DefaultColor(SqlEnvironmentKind kind) => kind switch
    { SqlEnvironmentKind.Dev => "#2E7D32", SqlEnvironmentKind.Test => "#B26A00", SqlEnvironmentKind.Prod => "#B71C1C", _ => "#6B7280" };
    internal bool Matches(SqlEnvironmentContext context) => Contains(context.Server, ServerContains) &&
        Contains(context.Database, DatabaseContains) && Contains(context.DocumentMoniker, DocumentContains);
    private static bool Contains(string? value, string pattern) => pattern.Length == 0 ||
        value?.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
    private static string Pattern(string value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        value = value.Trim();
        if (value.Length > 1024) throw new ArgumentException("Environment match fields cannot exceed 1024 characters.");
        return value;
    }
}

public sealed class SqlEnvironmentMatch
{
    internal SqlEnvironmentMatch(SqlEnvironmentContext context, SqlEnvironmentRule? rule)
    { Context = context; Rule = rule; }
    public SqlEnvironmentContext Context { get; }
    public SqlEnvironmentRule? Rule { get; }
    public SqlEnvironmentKind Kind => Rule?.Kind ?? SqlEnvironmentKind.Unknown;
    public string Label => Rule?.Label ?? "UNKNOWN";
    public string Color => Rule?.Color ?? SqlEnvironmentRule.DefaultColor(SqlEnvironmentKind.Unknown);
}

/// <summary>AND within a rule; PROD takes precedence over TEST and DEV across matching rules.</summary>
public sealed class SqlEnvironmentConfiguration
{
    public static SqlEnvironmentConfiguration Default { get; } = new();
    public SqlEnvironmentConfiguration(IEnumerable<SqlEnvironmentRule>? rules = null)
    {
        var copy = (rules ?? Array.Empty<SqlEnvironmentRule>()).ToArray();
        if (copy.Length > 100 || copy.Any(r => r == null))
            throw new ArgumentException("Use at most 100 non-null environment rules.", nameof(rules));
        Rules = Array.AsReadOnly(copy);
    }
    public IReadOnlyList<SqlEnvironmentRule> Rules { get; }
    public SqlEnvironmentMatch Match(SqlEnvironmentContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        return new(context, Rules.Where(r => r.Matches(context)).OrderByDescending(r => r.Kind).FirstOrDefault());
    }
    internal JArray ToJson() => new(Rules.Select(r => new JObject
    {
        ["environment"] = r.Label, ["serverContains"] = r.ServerContains,
        ["databaseContains"] = r.DatabaseContains, ["documentContains"] = r.DocumentContains, ["color"] = r.Color
    }));
    internal static SqlEnvironmentConfiguration Parse(JToken? token)
    {
        if (token == null) return Default;
        if (token is not JArray entries) throw new ArgumentException("Environment rules must be an array.");
        var rules = new List<SqlEnvironmentRule>();
        foreach (var entry in entries)
        {
            if (entry is not JObject row) throw new ArgumentException("An environment rule must be an object.");
            if (row.Properties().Any(p => p.Name is not ("environment" or "serverContains" or "databaseContains" or "documentContains" or "color")))
                throw new ArgumentException("Unknown environment rule field.");
            var label = Text(row, "environment");
            var kind = label.ToUpperInvariant() switch
            { "DEV" => SqlEnvironmentKind.Dev, "TEST" => SqlEnvironmentKind.Test, "PROD" => SqlEnvironmentKind.Prod,
                _ => throw new ArgumentException("Environment must be DEV, TEST or PROD.") };
            rules.Add(new(kind, Text(row, "serverContains"), Text(row, "databaseContains"),
                Text(row, "documentContains"), row["color"] == null ? null : Text(row, "color")));
        }
        return new(rules);
    }
    private static string Text(JObject row, string name) => row[name] == null ? "" :
        row[name]!.Type == JTokenType.String ? row.Value<string>(name)! :
            throw new ArgumentException("Environment field '" + name + "' must be a string.");
}
