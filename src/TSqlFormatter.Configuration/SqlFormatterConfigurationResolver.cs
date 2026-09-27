using TSqlFormatter.Core.Formatting;
using System.Text;

namespace TSqlFormatter.Configuration;

/// <summary>Applies built-in defaults, a JSON file, then explicit options in that order.</summary>
public sealed class SqlFormatterConfigurationResolver
{
    public const long MaxConfigurationBytes = 64L * 1024 * 1024;
    public const int MaxConfigurationCharacters = 16 * 1024 * 1024;
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

    /// <summary>Discovers and loads settings for an SQL file using the same search boundary as the CLI.</summary>
    public ConfigurationParseResult ResolveForSqlFile(string filePath)
    {
        string? configPath;
        try
        {
            configPath = new SqlFormatterConfigurationDiscovery().FindForFile(filePath);
            if (configPath is null) return Resolve();

            if (new FileInfo(configPath).Length > MaxConfigurationBytes)
                return FileError("TSF9001", $"{configPath}: configuration exceeds {MaxConfigurationBytes} bytes.");

            string json = File.ReadAllText(configPath, new UTF8Encoding(false, true));
            if (json.Length > MaxConfigurationCharacters)
                return FileError("TSF9001", $"{configPath}: configuration exceeds {MaxConfigurationCharacters} characters.");

            var result = Resolve(json);
            if (result.Succeeded) return result;
            var diagnostics = result.Diagnostics.Select(d => new FormatterDiagnostic(
                d.Code, $"{configPath}: {d.Message}", d.Severity, d.Span)).ToArray();
            return new ConfigurationParseResult(null, Array.AsReadOnly(diagnostics));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or ArgumentException)
        {
            return FileError("TSF9000", $"Cannot load SQL configuration: {exception.Message}");
        }
    }

    private static ConfigurationParseResult FileError(string code, string message)
    {
        var diagnostic = new FormatterDiagnostic(code, message, FormatterDiagnosticSeverity.Error);
        return new ConfigurationParseResult(null, Array.AsReadOnly(new[] { diagnostic }));
    }
}
