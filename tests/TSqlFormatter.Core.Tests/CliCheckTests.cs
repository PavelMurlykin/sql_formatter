using TSqlFormatter.Cli;

namespace TSqlFormatter.Core.Tests;

public sealed class CliCheckTests
{
    [Theory]
    [InlineData("SELECT Id\nFROM T", 0)]
    [InlineData("select Id from T", 1)]
    [InlineData("select from", 2)]
    public async Task Check_uses_expected_exit_code_and_never_changes_file(string source, int expectedCode)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-check-{Guid.NewGuid():N}.sql");
        await File.WriteAllTextAsync(path, source);
        var originalBytes = await File.ReadAllBytesAsync(path);
        try
        {
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--check" }, input, output, error);

            Assert.Equal(expectedCode, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
            if (expectedCode == 2) Assert.Contains("TSF1000", error.ToString());
            else Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Check_requires_a_file_path()
    {
        using var input = new StringReader("select 1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { "-", "--check" }, input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("TSF9000", error.ToString());
    }
}
