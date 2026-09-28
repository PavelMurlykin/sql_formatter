using System.Text;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Cli;

/// <summary>Runs the command-line formatter against redirected standard streams.</summary>
public static class SqlFormatterCli
{
    public const long MaxInputBytes = 64L * 1024 * 1024;
    public const int MaxInputCharacters = 16 * 1024 * 1024;

    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output,
        TextWriter error, CancellationToken cancellationToken = default)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (error is null) throw new ArgumentNullException(nameof(error));
        cancellationToken.ThrowIfCancellationRequested();

        if (args.Length == 1 && args[0] == "--help")
        {
            await output.WriteLineAsync("Usage: tsqlformat [file.sql | -] [--write | --check]");
            await output.WriteLineAsync("       tsqlformat <file-or-directory> [more-paths...] (--write | --check) [--exclude <relative-path>]...");
            await output.WriteLineAsync("Reads T-SQL from stdin or one file to stdout; batch mode checks or writes .sql files recursively.");
            return 0;
        }

        if (ShouldUseBatchMode(args))
            return await RunBatchAsync(args, input, output, error, cancellationToken);

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
                source = await ReadBoundedAsync(input, cancellationToken);
            }
        }
        catch (InputTooLargeException exception)
        {
            await error.WriteLineAsync($"TSF9001: {exception.Message}");
            return 2;
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
            var resolved = new SqlFormatterConfigurationResolver().ResolveForSqlFile(args[0]);
            cancellationToken.ThrowIfCancellationRequested();
            if (!resolved.Succeeded)
            {
                foreach (var diagnostic in resolved.Diagnostics)
                    await error.WriteLineAsync($"{diagnostic.Code}: {diagnostic.Message}");
                return 2;
            }

            options = resolved.Options!;
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

    private static bool ShouldUseBatchMode(string[] args)
    {
        if (args.Length == 0) return false;
        if (Directory.Exists(args[0])) return true;
        if (args.Contains("--exclude", StringComparer.Ordinal)) return true;
        return args.Count(arg => arg != "-" && !arg.StartsWith("--", StringComparison.Ordinal)) > 1;
    }

    private static async Task<int> RunBatchAsync(string[] args, TextReader input,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        var exclusions = new List<string>();
        var write = false;
        var check = false;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--write")
            {
                if (write || check) return await InvalidBatchArgumentsAsync(error);
                write = true;
            }
            else if (argument == "--check")
            {
                if (write || check) return await InvalidBatchArgumentsAsync(error);
                check = true;
            }
            else if (argument == "--exclude")
            {
                if (++index >= args.Length || !TryNormalizeExclusion(args[index], out var exclusion))
                    return await InvalidBatchArgumentsAsync(error);
                exclusions.Add(exclusion);
            }
            else if (argument == "-" || string.IsNullOrWhiteSpace(argument) ||
                     argument.StartsWith("-", StringComparison.Ordinal))
            {
                return await InvalidBatchArgumentsAsync(error);
            }
            else
            {
                paths.Add(argument);
            }
        }

        if ((!write && !check) || paths.Count == 0 ||
            (exclusions.Count > 0 && !paths.Any(Directory.Exists)))
            return await InvalidBatchArgumentsAsync(error);

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase :
            StringComparer.Ordinal;
        var files = new SortedSet<string>(comparer);
        var hasError = false;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var absolutePath = Path.GetFullPath(path);
                if (!Directory.Exists(absolutePath))
                {
                    files.Add(absolutePath);
                    continue;
                }

                var directoryFiles = FindSqlFiles(absolutePath, exclusions, comparer,
                    cancellationToken);
                if (directoryFiles.Count == 0)
                {
                    await error.WriteLineAsync($"TSF9000: No SQL files found in directory: {absolutePath}");
                    hasError = true;
                    continue;
                }

                foreach (var file in directoryFiles) files.Add(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
            {
                await error.WriteLineAsync($"TSF9000: Failed to enumerate SQL path '{path}': {exception.Message}");
                hasError = true;
            }
        }

        var needsFormatting = false;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var fileOutput = new StringWriter();
            using var fileError = new StringWriter();
            var result = await RunAsync(new[] { file, write ? "--write" : "--check" }, input,
                fileOutput, fileError, cancellationToken);
            if (result == 1)
            {
                needsFormatting = true;
                await error.WriteLineAsync($"Would reformat: {file}");
            }
            else if (result == 2)
            {
                hasError = true;
            }

            using var diagnosticReader = new StringReader(fileError.ToString());
            string? diagnostic;
            while ((diagnostic = await diagnosticReader.ReadLineAsync()) is not null)
                await error.WriteLineAsync($"{file}: {diagnostic}");
        }

        return hasError ? 2 : needsFormatting ? 1 : 0;
    }

    private static async Task<int> InvalidBatchArgumentsAsync(TextWriter error)
    {
        await error.WriteLineAsync("TSF9000: Batch mode requires paths and exactly one of --write or --check; use --help for usage.");
        return 2;
    }

    private static bool TryNormalizeExclusion(string value, out string exclusion)
    {
        exclusion = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
            value.IndexOfAny(new[] { '*', '?', ':' }) >= 0)
            return false;

        var segments = value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            return false;

        exclusion = string.Join("/", segments);
        return true;
    }

    private static List<string> FindSqlFiles(string root, IReadOnlyList<string> exclusions,
        StringComparer comparer, CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Directory links are not traversed.");

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (exclusions.Any(exclusion => IsExcluded(relative, exclusion, comparer)))
                    continue;

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else if (string.Equals(Path.GetExtension(entry), ".sql",
                             StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(entry);
                }
            }
        }

        return files;
    }

    private static bool IsExcluded(string relativePath, string exclusion, StringComparer comparer) =>
        comparer.Equals(relativePath, exclusion) ||
        relativePath.StartsWith(exclusion + "/", comparer == StringComparer.OrdinalIgnoreCase ?
            StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static async Task<(string Text, bool HasBom)> ReadUtf8FileAsync(string path,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 8192, useAsync: true);
        if (stream.Length > MaxInputBytes)
            throw new InputTooLargeException($"Input exceeds {MaxInputBytes} UTF-8 bytes.");

        var header = new byte[3];
        var headerLength = 0;
        while (headerLength < header.Length)
        {
            var read = await stream.ReadAsync(header.AsMemory(headerLength), cancellationToken);
            if (read == 0) break;
            headerLength += read;
        }
        var hasBom = headerLength == 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF;
        stream.Position = hasBom ? 3 : 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false, bufferSize: 8192);
        var text = await ReadBoundedAsync(reader, cancellationToken);
        return (text, hasBom);
    }

    private static async Task<string> ReadBoundedAsync(TextReader reader,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (text.Length > MaxInputCharacters - count)
                throw new InputTooLargeException($"Input exceeds {MaxInputCharacters} UTF-16 characters.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private static async Task WriteFileAtomicallyAsync(string path, string text, bool hasBom,
        CancellationToken cancellationToken)
    {
        var absolutePath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(absolutePath)!;
        var temporaryPath = Path.Combine(directory, $".tsqlformat-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(hasBom),
                             bufferSize: 8192))
            {
                await writer.WriteAsync(text.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, absolutePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed class InputTooLargeException : Exception
    {
        public InputTooLargeException(string message) : base(message) { }
    }
}
