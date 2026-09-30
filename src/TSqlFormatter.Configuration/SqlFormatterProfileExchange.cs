using System.Text;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Imports and exports portable native v1/v2 options, including every rule override.</summary>
public sealed class SqlFormatterProfileExchange
{
    public const long MaxProfileBytes = 1024 * 1024;
    private readonly SqlFormatterConfigurationSerializer serializer = new();

    public ConfigurationParseResult Import(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxProfileBytes)
                return Error("TSF9001", $"{path}: profile exceeds {MaxProfileBytes} bytes.");

            var json = File.ReadAllText(path, new UTF8Encoding(false, true));
            if (json.Length > MaxProfileBytes)
                return Error("TSF9001", $"{path}: profile exceeds {MaxProfileBytes} characters.");

            var result = serializer.Parse(json);
            if (result.Succeeded) return result;
            var diagnostics = result.Diagnostics.Select(d => new FormatterDiagnostic(
                d.Code, $"{path}: {d.Message}", d.Severity, d.Span)).ToArray();
            return new ConfigurationParseResult(null, Array.AsReadOnly(diagnostics));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or DecoderFallbackException or ArgumentException)
        {
            return Error("TSF9000", $"Cannot import profile: {ex.Message}");
        }
    }

    public void Export(string path, FormattingOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        File.WriteAllText(path, serializer.Serialize(options), new UTF8Encoding(false));
    }

    private static ConfigurationParseResult Error(string code, string message)
    {
        var diagnostic = new FormatterDiagnostic(code, message, FormatterDiagnosticSeverity.Error);
        return new ConfigurationParseResult(null, Array.AsReadOnly(new[] { diagnostic }));
    }
}
