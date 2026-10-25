namespace TSqlFormatter.Core.Snippets;

/// <summary>A reusable SQL template with optional $parameter$ placeholders in the body.</summary>
public sealed class SqlSnippet
{
    public SqlSnippet(string name, string shortcut, string body, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The snippet name must not be empty.", nameof(name));
        }

        if (body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        Name = name.Trim();
        Shortcut = shortcut.Trim();
        if (Shortcut.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The snippet shortcut must not contain whitespace.", nameof(shortcut));
        }

        Body = body;
        Description = description;
    }

    public string Name { get; }

    /// <summary>Short trigger word without whitespace; may be empty.</summary>
    public string Shortcut { get; }

    public string Body { get; }

    public string? Description { get; }

    /// <summary>Merge key: the shortcut when present, otherwise the name; case-insensitive.</summary>
    public string Key => Shortcut.Length > 0 ? Shortcut : Name;
}
