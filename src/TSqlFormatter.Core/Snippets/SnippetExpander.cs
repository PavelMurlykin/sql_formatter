using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Snippets;

/// <summary>A snippet body with $parameter$ placeholders resolved to text and navigation spans.</summary>
public sealed class ExpandedSnippet
{
    public ExpandedSnippet(string text, IReadOnlyList<SqlTextSpan> parameters)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    public string Text { get; }

    /// <summary>Parameter occurrences in navigation order, as offsets into <see cref="Text"/>.</summary>
    public IReadOnlyList<SqlTextSpan> Parameters { get; }
}

/// <summary>Expands snippet bodies by replacing $name$ placeholders with the name text.</summary>
public static class SnippetExpander
{
    public static ExpandedSnippet Expand(string body)
    {
        if (body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        var text = new System.Text.StringBuilder(body.Length);
        var parameters = new List<SqlTextSpan>();
        var index = 0;
        while (index < body.Length)
        {
            if (body[index] != '$')
            {
                text.Append(body[index]);
                index++;
                continue;
            }

            var closing = body.IndexOf('$', index + 1);
            if (closing <= index + 1 || !IsValidPlaceholder(body, index + 1, closing))
            {
                // A lone '$' or an invalid name stays literal.
                text.Append(body[index]);
                index++;
                continue;
            }

            var name = body.Substring(index + 1, closing - index - 1);
            var start = text.Length;
            text.Append(name);
            parameters.Add(new SqlTextSpan(start, name.Length));
            index = closing + 1;
        }

        return new ExpandedSnippet(text.ToString(), parameters);
    }

    private static bool IsValidPlaceholder(string body, int start, int closing)
    {
        for (var index = start; index < closing; index++)
        {
            if (char.IsWhiteSpace(body[index]))
            {
                return false;
            }
        }

        return true;
    }
}
