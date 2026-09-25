using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Applies built-in defaults, a JSON file, then explicit options in that order.</summary>
public sealed class SqlFormatterConfigurationResolver
{
    private readonly SqlFormatterConfigurationSerializer _serializer = new();
    private readonly FormattingProfileCatalog _profiles;

    public SqlFormatterConfigurationResolver(FormattingProfileCatalog? profiles = null)
    {
        _profiles = profiles ?? new FormattingProfileCatalog();
    }

    public ConfigurationParseResult Resolve(
        string? fileJson = null,
        FormattingOptionsOverrides? explicitOptions = null,
        string profileId = "Default")
    {
        if (!_profiles.TryGet(profileId, out var profile))
        {
            var diagnostic = new FormatterDiagnostic("TSF2000",
                $"Unknown formatting profile '{profileId}'.", FormatterDiagnosticSeverity.Error);
            return new ConfigurationParseResult(null, Array.AsReadOnly(new[] { diagnostic }));
        }

        var file = fileJson is null
            ? new ConfigurationParseResult(profile!.Options, Array.Empty<FormatterDiagnostic>())
            : _serializer.Parse(fileJson, profile!.Options);
        if (!file.Succeeded) return file;

        var options = explicitOptions?.ApplyTo(file.Options!) ?? file.Options!;
        return new ConfigurationParseResult(options, Array.Empty<FormatterDiagnostic>());
    }
}
