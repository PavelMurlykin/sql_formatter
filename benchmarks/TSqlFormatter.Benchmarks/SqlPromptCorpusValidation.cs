using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Benchmarks;

/// <summary>Read-only comparison with a local SQL Prompt corpus. SQL is never executed or overwritten.</summary>
internal static class SqlPromptCorpusValidation
{
    public static void Run(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("Expected: directory profile.json output-directory [git|last-write|all] [limit]");
        var directory = Path.GetFullPath(args[0]);
        var profile = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        var mode = args.ElementAtOrDefault(3) ?? "git";
        var imported = new SqlFormatterProfileExchange().Import(profile);
        var options = imported.Options ?? throw new ArgumentException(string.Join("; ", imported.Diagnostics.Select(d => d.Message)));
        var all = Directory.GetFiles(directory, "*.sql", SearchOption.AllDirectories);
        var eligible = mode switch
        {
            "all" => all.Where(f => !Path.GetRelativePath(directory, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) || p.Equals("obj", StringComparison.OrdinalIgnoreCase) || p.Equals(".vs", StringComparison.OrdinalIgnoreCase))),
            "last-write" => all.Where(f => File.GetLastWriteTime(f).Year == 2026),
            "git" => GitFiles(directory, all),
            _ => throw new ArgumentException("Date source must be git, last-write or all.")
        };
        var files = eligible.OrderBy(f => new FileInfo(f).Length).ThenBy(f => f, StringComparer.Ordinal).ToArray();
        if (args.Length > 4) files = files.Take(int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Directory.CreateDirectory(output);
        var results = new ConcurrentBag<string[]>();
        var completed = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, file =>
        {
            try
            {
                var timer = Stopwatch.StartNew();
                var source = File.ReadAllText(file, new UTF8Encoding(false, true));
                var parser = new ScriptDomSqlParser();
                var formatter = new ScriptDomSqlFormatter(parser);
                var before = parser.Parse(source, SqlDialectVersion.Auto);
                var result = before.ParseSucceeded ? formatter.Format(source, options, new FormatRequest()) : new FormatResult(source, false, false);
                var after = parser.Parse(result.Text, SqlDialectVersion.Auto);
                var stable = !before.ParseSucceeded || formatter.Format(result.Text, options, new FormatRequest()).Text == result.Text;
                var tokens = !before.ParseSucceeded ? source == result.Text : SqlTokenSafety.PreservesTokens(before, after, options);
                var normalized = Normalize(source);
                var actual = Normalize(result.Text);
                var multiline = before.Tokens.Any(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment
                    or TSqlTokenType.MultilineComment or TSqlTokenType.EndOfFile) && t.Text.IndexOfAny(['\r', '\n']) >= 0);
                var status = !before.ParseSucceeded ? "source_parse_error" : multiline ? "preserved_multiline_token"
                    : result.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error) ? "formatter_error"
                    : result.Diagnostics.Any(d => d.Code == "TSF3006") ? "preserved_unstable_layout"
                    : result.Diagnostics.Any(d => d.Code == "TSF3007") ? "preserved_token_validation"
                    : !tokens ? "token_mismatch" : !stable ? "not_idempotent" : normalized == actual ? "exact" : "different";
                var name = Path.GetRelativePath(directory, file);
                var beforeTokens = before.Tokens.Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).ToArray();
                var afterTokens = after.Tokens.Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).ToArray();
                var agreements = 0;
                if (before.ParseSucceeded && !multiline && tokens && !status.StartsWith("preserved_", StringComparison.Ordinal) && beforeTokens.Length == afterTokens.Length)
                    for (var i = 1; i < beforeTokens.Length; i++)
                    {
                        var gapBefore = source.Substring(beforeTokens[i - 1].Offset + beforeTokens[i - 1].Text.Length,
                            beforeTokens[i].Offset - beforeTokens[i - 1].Offset - beforeTokens[i - 1].Text.Length);
                        var gapAfter = result.Text.Substring(afterTokens[i - 1].Offset + afterTokens[i - 1].Text.Length,
                            afterTokens[i].Offset - afterTokens[i - 1].Offset - afterTokens[i - 1].Text.Length);
                        if (gapBefore.Replace("\r\n", "\n") == gapAfter.Replace("\r\n", "\n")) agreements++;
                    }
                results.Add([name, status, new FileInfo(file).Length.ToString(), Hash(File.ReadAllBytes(file)),
                    before.ParseSucceeded.ToString(), after.ParseSucceeded.ToString(), tokens.ToString(), stable.ToString(),
                    System.Text.RegularExpressions.Regex.Matches(normalized, "\n").Count.ToString(),
                    System.Text.RegularExpressions.Regex.Matches(actual, "\n").Count.ToString(), timer.ElapsedMilliseconds.ToString(),
                    string.Join(",", result.Diagnostics.Select(d => d.Code).Distinct()), before.ParseSucceeded && !multiline && !status.StartsWith("preserved_", StringComparison.Ordinal) ? Math.Max(0, beforeTokens.Length - 1).ToString() : "0", agreements.ToString()]);
                if (status is "different" or "not_idempotent" or "token_mismatch" or "formatter_error")
                {
                    // Keep local review material out of tracked documentation and test corpora.
                    var actualPath = Path.Combine(output, name + ".actual");
                    Directory.CreateDirectory(Path.GetDirectoryName(actualPath)!);
                    File.WriteAllText(actualPath, result.Text, new UTF8Encoding(false));
                    if (!stable) File.WriteAllText(Path.Combine(output, name + ".second"), formatter.Format(result.Text, options, new FormatRequest()).Text);
                }
            }
            catch (DecoderFallbackException)
            {
                var bytes = File.ReadAllBytes(file);
                results.Add([Path.GetRelativePath(directory, file), "source_encoding_unsupported", bytes.Length.ToString(), Hash(bytes), "False", "False", "True", "True", "0", "0", "0", "strict_UTF8_decode_failed", "0", "0"]);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                results.Add([Path.GetRelativePath(directory, file), "source_read_error", "0", "-", "False", "False", "False", "False", "0", "0", "0", error.GetType().Name, "0", "0"]);
            }
            var done = Interlocked.Increment(ref completed);
            if (done % (files.Length > 1000 ? 500 : 25) == 0 || done == files.Length) Console.WriteLine($"Compared {done}/{files.Length}");
        });
        File.WriteAllLines(Path.Combine(output, "files.tsv"), new[] { "file\tstatus\tbytes\tsource_sha256\tparse_before\tparse_after\ttokens_preserved\tidempotent\tsource_lines\toutput_lines\telapsed_ms\tdiagnostics\ttoken_gaps\tmatching_gaps" }
            .Concat(results.OrderBy(r => r[0], StringComparer.Ordinal).Select(r => string.Join("\t", r))), new UTF8Encoding(false));
        var summary = $"Year: {(mode == "all" ? "all" : "2026")}\nDate source: {mode}\nSelected files: {files.Length}\nProfile SHA256: {Hash(File.ReadAllBytes(profile))}\n"
            + string.Join("\n", results.GroupBy(r => r[1]).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}")) + "\n";
        File.WriteAllText(Path.Combine(output, "summary.txt"), summary, new UTF8Encoding(false));
        Console.Write(summary);
        Environment.ExitCode = results.Any(r => r[1] is "token_mismatch" or "not_idempotent" or "formatter_error" or "source_read_error") ? 1 : 0;
    }

    private static IEnumerable<string> GitFiles(string directory, string[] all)
    {
        var root = Git(directory, "rev-parse", "--show-toplevel").Trim();
        var relative = Path.GetRelativePath(root, directory).Replace('\\', '/');
        var names = Git(root, "log", "--since=2026-01-01T00:00:00+03:00", "--until=2026-12-31T23:59:59+03:00",
            "--format=", "--name-only", "--", relative).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(f => Path.GetFullPath(Path.Combine(root, f))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(names.Contains);
    }
    private static string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        // A per-command trust override is confined to this user-specified read-only repository.
        var trustedRoot = directory;
        while (!Directory.Exists(Path.Combine(trustedRoot, ".git")) && !File.Exists(Path.Combine(trustedRoot, ".git")))
            trustedRoot = Directory.GetParent(trustedRoot)?.FullName ?? throw new ArgumentException("Directory is outside a Git checkout.");
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("safe.directory=" + trustedRoot.Replace('\\', '/'));
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("core.quotepath=false");
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(directory);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var text = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException(error);
        return text;
    }
    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
