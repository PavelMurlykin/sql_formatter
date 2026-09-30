using System.Text;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Configuration;

/// <summary>Reads only common whitespace settings from [*] and [*.sql] sections.</summary>
internal sealed class SqlEditorConfigResolver
{
    private const long MaxBytes = 1024 * 1024;

    public ConfigurationParseResult ResolveForSqlFile(string filePath, FormattingOptions baseline)
    {
        try
        {
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in FindConfigurationPaths(filePath))
                ReadApplicableProperties(File.ReadAllText(path, new UTF8Encoding(false, true)), settings);

            var general = baseline.General;
            var indent = baseline.Indent;
            int width = general.MaxLineWidth;
            var ending = general.LineEnding;
            bool finalNewline = general.FinalNewline;
            int indentSize = indent.Size;
            bool useTabs = indent.UseTabs;

            if (settings.TryGetValue("end_of_line", out var eol))
                ending = eol.ToLowerInvariant() switch
                {
                    "lf" => DocLineEnding.Lf,
                    "crlf" => DocLineEnding.CrLf,
                    "cr" => DocLineEnding.Cr,
                    _ => ending
                };
            if (settings.TryGetValue("insert_final_newline", out var newline)
                && bool.TryParse(newline, out var parsedNewline)) finalNewline = parsedNewline;
            if (settings.TryGetValue("indent_size", out var size)
                && int.TryParse(size, out int parsedSize) && parsedSize >= 0 && parsedSize <= 32)
                indentSize = parsedSize;
            if (settings.TryGetValue("indent_style", out var style))
            {
                if (style.Equals("tab", StringComparison.OrdinalIgnoreCase)) useTabs = true;
                if (style.Equals("space", StringComparison.OrdinalIgnoreCase)) useTabs = false;
            }

            return new ConfigurationParseResult(baseline.With(
                general: new GeneralOptions(width, ending, finalNewline),
                indent: new IndentOptions(indentSize, useTabs)), Array.Empty<FormatterDiagnostic>());
        }
        catch (ConfigurationSizeException exception) { return Failure("TSF9001", exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or ArgumentException)
        {
            return Failure("TSF9000", $"Cannot load .editorconfig: {exception.Message}");
        }
    }

    internal static IReadOnlyList<string> FindConfigurationPaths(string filePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var files = new List<string>();
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, ".editorconfig");
            if (File.Exists(path))
            {
                files.Add(path);
                if (new FileInfo(path).Length > MaxBytes)
                    throw new ConfigurationSizeException($"{path}: .editorconfig exceeds {MaxBytes} bytes.");
                if (HasRootMarker(File.ReadAllText(path, new UTF8Encoding(false, true)))) break;
            }
            directory = directory.Parent;
        }
        files.Reverse();
        return files.AsReadOnly();
    }

    private sealed class ConfigurationSizeException : IOException
    {
        public ConfigurationSizeException(string message) : base(message) { }
    }

    private static bool HasRootMarker(string content)
    {
        foreach (var raw in SplitLines(content))
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal)) return false;
            if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                continue;
            int equals = line.IndexOf('=');
            if (equals > 0 && line.Substring(0, equals).Trim().Equals("root", StringComparison.OrdinalIgnoreCase)
                && line.Substring(equals + 1).Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void ReadApplicableProperties(string content, Dictionary<string, string> settings)
    {
        bool applies = false;
        foreach (var raw in SplitLines(content))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)
                || line.StartsWith(";", StringComparison.Ordinal)) continue;
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                var section = line.Substring(1, line.Length - 2);
                applies = section == "*" || section.Equals("*.sql", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!applies) continue;
            int equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line.Substring(0, equals).Trim().ToLowerInvariant();
            if (key is not ("indent_style" or "indent_size" or "end_of_line" or "insert_final_newline"))
                continue;
            var value = line.Substring(equals + 1).Trim();
            if (value.Equals("unset", StringComparison.OrdinalIgnoreCase)) settings.Remove(key);
            else settings[key] = value;
        }
    }

    private static string[] SplitLines(string text) => text.Split(new[] { "\r\n", "\n", "\r" },
        StringSplitOptions.None);

    private static ConfigurationParseResult Failure(string code, string message) => new(null,
        Array.AsReadOnly(new[] { new FormatterDiagnostic(code, message, FormatterDiagnosticSeverity.Error) }));
}
