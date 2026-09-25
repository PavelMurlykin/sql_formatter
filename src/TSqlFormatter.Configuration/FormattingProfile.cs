using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>A named, immutable set of formatting options.</summary>
public sealed class FormattingProfile
{
    public FormattingProfile(string id, string name, FormattingOptions options)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A profile ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A profile name is required.", nameof(name));
        Id = id;
        Name = name;
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Id { get; }
    public string Name { get; }
    public FormattingOptions Options { get; }
}
