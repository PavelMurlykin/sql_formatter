using System.Diagnostics;
using System.Text;
using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;

namespace TSqlFormatter.Core.Tests;

public sealed class CliProcessIntegrationTests
{
    [Fact]
    public async Task Stdin_round_trip_uses_stdout_and_correct_exit_code()
    {
        var run = await RunProcessAsync("select Id from T");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("SELECT Id\nFROM T", run.Stdout);
        Assert.Equal(string.Empty, run.Stderr);
    }

    [Fact]
    public async Task File_stdout_write_check_and_config_discovery_work_end_to_end()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tsqlformatter-process-{Guid.NewGuid():N}");
        var repo = Path.Combine(root, "repo");
        var sqlDirectory = Path.Combine(repo, "sql");
        Directory.CreateDirectory(sqlDirectory);
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var path = Path.Combine(sqlDirectory, "query.sql");
        var configPath = Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName);
        await File.WriteAllTextAsync(path, "SELECT Id FROM T");
        await File.WriteAllTextAsync(configPath,
            """{"version":1,"keywords":{"case":"lower"}}""");
        try
        {
            var display = await RunProcessAsync(null, path);
            Assert.Equal(0, display.ExitCode);
            Assert.Equal("select Id\nfrom T", display.Stdout);
            Assert.Equal("SELECT Id FROM T", await File.ReadAllTextAsync(path));

            var needsFormatting = await RunProcessAsync(null, path, "--check");
            Assert.Equal(1, needsFormatting.ExitCode);
            Assert.Equal(string.Empty, needsFormatting.Stdout);

            var write = await RunProcessAsync(null, path, "--write");
            Assert.Equal(0, write.ExitCode);
            Assert.Equal(string.Empty, write.Stdout);
            Assert.Equal("select Id\nfrom T", await File.ReadAllTextAsync(path));

            var formatted = await RunProcessAsync(null, path, "--check");
            Assert.Equal(0, formatted.ExitCode);
            Assert.Equal(string.Empty, formatted.Stdout);

            await File.WriteAllTextAsync(configPath, """{"version":1,"keywords":{"case":"invalid"}}""");
            var badConfig = await RunProcessAsync(null, path, "--write");
            Assert.Equal(2, badConfig.ExitCode);
            Assert.Contains("TSF2000", badConfig.Stderr);
            Assert.Equal(string.Empty, badConfig.Stdout);
            Assert.Equal("select Id\nfrom T", await File.ReadAllTextAsync(path));
        }
        finally
        {
            var absoluteRoot = Path.GetFullPath(root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!absoluteRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Integration fixture escaped the temp directory.");
            Directory.Delete(absoluteRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_sql_process_returns_two_without_stdout()
    {
        var run = await RunProcessAsync("select from");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(string.Empty, run.Stdout);
        Assert.Contains("TSF1000", run.Stderr);
    }

    private static async Task<ProcessResult> RunProcessAsync(string? stdin, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add(typeof(SqlFormatterCli).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI did not start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (stdin is not null) await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
