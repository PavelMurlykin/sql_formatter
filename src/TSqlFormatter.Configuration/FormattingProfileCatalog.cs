using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Built-in and application-supplied named profiles.</summary>
public sealed class FormattingProfileCatalog
{
    private readonly Dictionary<string, FormattingProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public FormattingProfileCatalog(IEnumerable<FormattingProfile>? customProfiles = null)
    {
        Add(new FormattingProfile("Default", "Default", FormattingOptions.Default));
        Add(new FormattingProfile("Compact", "Compact", FormattingOptions.Default.With(
            general: new GeneralOptions(maxLineWidth: 120))));
        Add(new FormattingProfile("Expanded", "Expanded", FormattingOptions.Default.With(
            general: new GeneralOptions(maxLineWidth: 80),
            select: new SelectOptions(SelectColumnLayout.OnePerLine),
            clauses: new QueryClauseOptions(
                ClauseItemLayout.OnePerLine, ClauseItemLayout.OnePerLine))));

        if (customProfiles is not null)
        {
            foreach (var profile in customProfiles)
                Add(profile ?? throw new ArgumentException("Profiles cannot contain null.", nameof(customProfiles)));
        }

        Profiles = Array.AsReadOnly(_profiles.Values.ToArray());
    }

    public IReadOnlyList<FormattingProfile> Profiles { get; }

    public bool TryGet(string? id, out FormattingProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            profile = null;
            return false;
        }

        return _profiles.TryGetValue(id!, out profile);
    }

    private void Add(FormattingProfile profile)
    {
        if (_profiles.ContainsKey(profile.Id))
            throw new ArgumentException($"Duplicate profile ID '{profile.Id}'.", nameof(profile));
        _profiles.Add(profile.Id, profile);
    }
}
