using System.Text;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Cli;

/// <summary>Runs the command-line formatter against redirected standard streams.</summary>
public static class SqlFormatterCli
{
    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output,
        TextWriter error, CancellationToken cancellationToken = default)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (error is null) throw new ArgumentNullException(nameof(error));

        if (args.Length == 1 && args[0] == "--help")
        {
            await output.WriteLineAsync("Usage: tsqlformat [file.sql [--write | --check] | -]");
            await output.WriteLineAsync("Reads T-SQL from one file or stdin and writes formatted SQL to stdout.");
            return 0;
        }

        var isFile = args.Length > 0 && args[0] != "-";
        var write = args.Length == 2 && args[1] == "--write";
        var check = args.Length == 2 && args[1] == "--check";
        if (args.Length > 2 || (args.Length == 2 && (!isFile || (!write && !check))) ||
            (args.Length > 0 &&
             (string.IsNullOrWhiteSpace(args[0]) ||
              (args[0].StartsWith("-", StringComparison.Ordinal) && args[0] != "-"))))
        {
            await error.WriteLineAsync("TSF9000: Unsupported arguments. Use --help for usage.");
            return 2;
        }

        string source;
        var hasBom = false;
        try
        {
            if (isFile)
            {
                (source, hasBom) = await ReadUtf8FileAsync(args[0], cancellationToken);
            }
            else
            {
                source = await input.ReadToEndAsync();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException)
        {
            await error.WriteLineAsync($"TSF9000: Failed to read SQL input: {exception.Message}");
            return 2;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var options = FormattingOptions.Default;
        if (isFile)
        {
            string? configPath;
            try
            {
                configPath = new SqlFormatterConfigurationDiscovery().FindForFile(args[0]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await error.WriteLineAsync($"TSF9000: Failed to find configuration: {exception.Message}");
                return 2;
            }

            if (configPath is not null)
            {
                string configJson;
                try
                {
                    (configJson, _) = await ReadUtf8FileAsync(configPath, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or DecoderFallbackException)
                {
                    await error.WriteLineAsync($"TSF9000: Failed to read configuration '{configPath}': {exception.Message}");
                    return 2;
                }

                var resolved = new SqlFormatterConfigurationResolver().Resolve(configJson);
                if (!resolved.Succeeded)
                {
                    foreach (var diagnostic in resolved.Diagnostics)
                        await error.WriteLineAsync($"{diagnostic.Code}: {configPath}: {diagnostic.Message}");
                    return 2;
                }

                options = resolved.Options!;
            }
        }

        var result = new ScriptDomSqlFormatter().Format(
            source, options, new FormatRequest(), cancellationToken);
        foreach (var diagnostic in result.Diagnostics)
            await error.WriteLineAsync($"{diagnostic.Code}: {diagnostic.Message}");

        if (!result.ParseSucceeded || result.Diagnostics.Any(d =>
            d.Severity == FormatterDiagnosticSeverity.Error))
            return 2;

        if (check) return result.Changed ? 1 : 0;

        if (write)
        {
            if (!result.Changed) return 0;
            try
            {
                await WriteFileAtomicallyAsync(args[0], result.Text, hasBom, cancellationToken);
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await error.WriteLineAsync($"TSF9000: Failed to write SQL file: {exception.Message}");
                return 2;
            }
        }

        await output.WriteAsync(result.Text);
        return 0;
    }

    private static async Task<(string Text, bool HasBom)> ReadUtf8FileAsync(string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false, true).GetString(bytes, hasBom ? 3 : 0,
            bytes.Length - (hasBom ? 3 : 0));
        return (text, hasBom);
    }

    private static async Task WriteFileAtomicallyAsync(string path, string text, bool hasBom,
        CancellationToken cancellationToken)
    {
        var absolutePath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(absolutePath)!;
        var temporaryPath = Path.Combine(directory, $".tsqlformat-{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = new UTF8Encoding(hasBom).GetBytes(text);
            if (hasBom)
                bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray();
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, absolutePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
