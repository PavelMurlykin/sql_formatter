using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Applies built-in defaults, a JSON file, then explicit options in that order.</summary>
public sealed class SqlFormatterConfigurationResolver
{
    private readonly SqlFormatterConfigurationSerializer _serializer = new();

    public ConfigurationParseResult Resolve(
        string? fileJson = null,
        FormattingOptionsOverrides? explicitOptions = null)
    {
        var file = fileJson is null
            ? new ConfigurationParseResult(FormattingOptions.Default, Array.Empty<FormatterDiagnostic>())
            : _serializer.Parse(fileJson);
        if (!file.Succeeded) return file;

        var options = explicitOptions?.ApplyTo(file.Options!) ?? file.Options!;
        return new ConfigurationParseResult(options, Array.Empty<FormatterDiagnostic>());
    }
}
