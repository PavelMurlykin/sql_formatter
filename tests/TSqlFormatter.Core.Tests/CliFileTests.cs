using TSqlFormatter.Cli;

namespace TSqlFormatter.Core.Tests;

public sealed class CliFileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Formats_one_file_to_stdout_without_modifying_it(bool withBom)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-{Guid.NewGuid():N}.sql");
        var source = (withBom ? "\uFEFF" : string.Empty) + "select Id from T";
        await File.WriteAllTextAsync(path, source);
        var originalBytes = await File.ReadAllBytesAsync(path);
        try
        {
            using var input = new StringReader("not used");
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path }, input, output, error);

            Assert.Equal(0, exitCode);
            Assert.Equal("SELECT Id\nFROM T", output.ToString());
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Missing_file_reports_error_without_sql_output()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-missing-{Guid.NewGuid():N}.sql");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { path }, input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("TSF9000", error.ToString());
    }

    [Fact]
    public async Task Invalid_sql_file_is_left_unchanged_and_produces_no_stdout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-invalid-{Guid.NewGuid():N}.sql");
        const string source = "select from";
        await File.WriteAllTextAsync(path, source);
        try
        {
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path }, input, output, error);

            Assert.Equal(2, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("TSF1000", error.ToString());
            Assert.Equal(source, await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Multiple_file_arguments_are_rejected()
    {
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { "one.sql", "two.sql" }, input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("TSF9000", error.ToString());
    }
}
