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
            await output.WriteLineAsync("Usage: tsqlformat [file.sql | -]");
            await output.WriteLineAsync("Reads T-SQL from one file or stdin and writes formatted SQL to stdout.");
            return 0;
        }

        if (args.Length > 1 || (args.Length == 1 &&
            (string.IsNullOrWhiteSpace(args[0]) ||
             (args[0].StartsWith("-", StringComparison.Ordinal) && args[0] != "-"))))
        {
            await error.WriteLineAsync("TSF9000: Unsupported arguments. Use --help for usage.");
            return 2;
        }

        string source;
        try
        {
            source = args.Length == 0 || args[0] == "-"
                ? await input.ReadToEndAsync()
                : await File.ReadAllTextAsync(args[0], cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"TSF9000: Failed to read SQL input: {exception.Message}");
            return 2;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = new ScriptDomSqlFormatter().Format(
            source, FormattingOptions.Default, new FormatRequest(), cancellationToken);
        foreach (var diagnostic in result.Diagnostics)
            await error.WriteLineAsync($"{diagnostic.Code}: {diagnostic.Message}");

        if (!result.ParseSucceeded || result.Diagnostics.Any(d =>
            d.Severity == FormatterDiagnosticSeverity.Error))
            return 2;

        await output.WriteAsync(result.Text);
        return 0;
    }
}
