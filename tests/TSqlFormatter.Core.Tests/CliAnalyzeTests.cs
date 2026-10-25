using System.Text;
using TSqlFormatter.Cli;

namespace TSqlFormatter.Core.Tests;

public sealed class CliAnalyzeTests
{
    [Fact]
    public async Task Analyze_file_reports_findings_to_stdout_with_exit_code_1()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = await WriteFileAsync(directory, "violating.sql", "SELECT *\nFROM dbo.T;");
            var original = await File.ReadAllTextAsync(path);

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--analyze" }, input, output, error);

            Assert.Equal(1, exitCode);
            Assert.Contains($"{path}(1,8): TSQL001 [Warning]", output.ToString());
            Assert.Empty(error.ToString());
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Analyze_clean_file_exits_zero_without_output()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = await WriteFileAsync(directory, "clean.sql", "SELECT Id FROM dbo.T WHERE Id = 1;\n");

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--analyze" }, input, output, error);

            Assert.Equal(0, exitCode);
            Assert.Empty(output.ToString());
            Assert.Empty(error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Analyze_stdin_reports_findings_with_dash_path()
    {
        using var input = new StringReader("DELETE FROM dbo.T;");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { "-", "--analyze" }, input, output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal("-(1,1): TSQL005 [Error] DELETE without a WHERE clause." + Environment.NewLine,
            output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Analyze_parse_error_returns_two()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = await WriteFileAsync(directory, "broken.sql", "select from");

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--analyze" }, input, output, error);

            Assert.Equal(2, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("TSF1000", error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("--analyze")]
    [InlineData("a.sql b.sql --analyze --check")]
    [InlineData("--analyze --analyze")]
    [InlineData("- file.sql --analyze")]
    [InlineData("--exclude sub --analyze")]
    public async Task Analyze_rejects_invalid_argument_combinations(string joinedArgs)
    {
        var args = joinedArgs.Split(' ');
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(args, input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("TSF9000", error.ToString());
    }

    [Fact]
    public async Task Analyze_directory_reports_each_file_and_honors_exclude()
    {
        var directory = CreateTempDirectory();
        try
        {
            await WriteFileAsync(directory, "clean.sql", "SELECT Id FROM dbo.T;\n");
            var nested = Path.Combine(directory, "sub");
            Directory.CreateDirectory(nested);
            var violating = await WriteFileAsync(nested, "violating.sql", "SELECT * FROM dbo.T;\n");

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(
                new[] { directory, "--analyze", "--exclude", "sub" }, input, output, error);

            Assert.Equal(0, exitCode);
            Assert.Empty(output.ToString());
            Assert.Empty(error.ToString());

            using var outputAll = new StringWriter();
            using var errorAll = new StringWriter();
            exitCode = await SqlFormatterCli.RunAsync(new[] { directory, "--analyze" }, input, outputAll, errorAll);

            Assert.Equal(1, exitCode);
            Assert.Contains($"{violating}(1,8): TSQL001 [Warning]", outputAll.ToString());
            Assert.Empty(errorAll.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Analyze_applies_analysis_configuration_from_repository()
    {
        var directory = CreateTempDirectory();
        try
        {
            await WriteFileAsync(directory, ".tsqlformatter.json",
                "{\"version\":1,\"analysis\":{\"disabled\":[\"TSQL001\"],\"severity\":{\"TSQL005\":\"warning\"}}}");
            var violating = await WriteFileAsync(directory, "violating.sql", "SELECT * FROM dbo.T;\n");
            var deleting = await WriteFileAsync(directory, "deleting.sql", "DELETE FROM dbo.T;\n");

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { directory, "--analyze" }, input, output, error);

            Assert.Equal(1, exitCode);
            var stdout = output.ToString();
            Assert.DoesNotContain(violating, stdout);
            Assert.Contains($"{deleting}(1,1): TSQL005 [Warning]", stdout);
            Assert.Empty(error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Analyze_invalid_analysis_configuration_returns_two()
    {
        var directory = CreateTempDirectory();
        try
        {
            await WriteFileAsync(directory, ".tsqlformatter.json",
                "{\"version\":1,\"analysis\":{\"severity\":{\"TSQL004\":\"fatal\"}}}");
            var clean = await WriteFileAsync(directory, "clean.sql", "SELECT Id FROM dbo.T;\n");

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { clean, "--analyze" }, input, output, error);

            Assert.Equal(2, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("TSF2000", error.ToString());
            Assert.Contains("info, warning, error", error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory() => Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), $"tsqlformatter-analyze-{Guid.NewGuid():N}")).FullName;

    private static async Task<string> WriteFileAsync(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        return path;
    }
}
