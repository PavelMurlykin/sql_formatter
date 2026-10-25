using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TSqlFormatter.Core.Snippets;

/// <summary>Reads snippet JSON: one object or an array of objects with name, shortcut, description and body.</summary>
public static class SnippetJson
{
    public static bool TryParse(string json, out IReadOnlyList<SqlSnippet> snippets, out string? error)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        snippets = Array.Empty<SqlSnippet>();
        try
        {
            snippets = Parse(json);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static IReadOnlyList<SqlSnippet> Parse(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        var root = JToken.Parse(json);
        IEnumerable<JToken> tokens = root.Type == JTokenType.Array
            ? ((JArray)root).Children()
            : new JToken[] { root };
        var snippets = new List<SqlSnippet>();
        foreach (var token in tokens)
        {
            if (token is not JObject properties)
            {
                throw new JsonSerializationException("Each snippet must be a JSON object.");
            }

            var name = ReadString(properties, "name");
            var shortcut = ReadString(properties, "shortcut");
            var description = ReadString(properties, "description");
            var body = properties["body"]?.Type == JTokenType.String
                ? properties["body"]!.Value<string>()
                : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new JsonSerializationException("A snippet requires a non-empty 'name'.");
            }

            if (body is null)
            {
                throw new JsonSerializationException("A snippet requires a string 'body'.");
            }

            if (shortcut != null && shortcut.Any(char.IsWhiteSpace))
            {
                throw new JsonSerializationException("The snippet 'shortcut' must not contain whitespace.");
            }

            snippets.Add(new SqlSnippet(name!, shortcut ?? string.Empty, body, description));
        }

        if (snippets.Count == 0)
        {
            throw new JsonSerializationException("No snippets found in the JSON document.");
        }

        return snippets;
    }

    private static string? ReadString(JObject properties, string name)
    {
        var value = properties[name];
        return value?.Type == JTokenType.String ? value.Value<string>() : null;
    }
}
