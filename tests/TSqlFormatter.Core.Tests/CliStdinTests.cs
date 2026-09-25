using TSqlFormatter.Cli;

namespace TSqlFormatter.Core.Tests;

public sealed class CliStdinTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Formats_stdin_to_stdout_without_extra_text(bool explicitDash)
    {
        using var input = new StringReader("select Id from T");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var args = explicitDash ? new[] { "-" } : Array.Empty<string>();

        var exitCode = await SqlFormatterCli.RunAsync(args, input, output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal("SELECT Id\nFROM T", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Invalid_sql_reports_error_without_writing_sql_to_stdout()
    {
        using var input = new StringReader("select from");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(Array.Empty<string>(), input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("TSF1000", error.ToString());
    }

    [Fact]
    public async Task Unsupported_arguments_are_reported()
    {
        using var input = new StringReader("select 1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { "--write" }, input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("TSF9000", error.ToString());
    }

    [Fact]
    public async Task Help_is_available_without_input()
    {
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { "--help" }, input, output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("stdin", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }
}
