using System.Text;
using TSqlFormatter.Cli;

namespace TSqlFormatter.Core.Tests;

public sealed class CliWriteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_formats_file_in_place_and_preserves_utf8_bom(bool withBom)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-write-{Guid.NewGuid():N}.sql");
        var original = new UTF8Encoding(false).GetBytes("select Id from T");
        if (withBom) original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(original).ToArray();
        await File.WriteAllBytesAsync(path, original);
        try
        {
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--write" }, input, output, error);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal(string.Empty, error.ToString());
            var expected = new UTF8Encoding(false).GetBytes("SELECT Id\nFROM T");
            if (withBom) expected = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(expected).ToArray();
            Assert.Equal(expected, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Write_does_not_replace_already_formatted_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-write-{Guid.NewGuid():N}.sql");
        const string source = "SELECT Id\nFROM T";
        await File.WriteAllTextAsync(path, source);
        var before = File.GetLastWriteTimeUtc(path);
        try
        {
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--write" }, input, output, error);

            Assert.Equal(0, exitCode);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            Assert.Equal(before, File.GetLastWriteTimeUtc(path));
            Assert.Equal(string.Empty, output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Write_rejects_invalid_sql_without_changing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-write-{Guid.NewGuid():N}.sql");
        const string source = "select from";
        await File.WriteAllTextAsync(path, source);
        try
        {
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--write" }, input, output, error);

            Assert.Equal(2, exitCode);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("TSF1000", error.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Write_requires_a_file_path()
    {
        using var input = new StringReader("select 1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { "-", "--write" }, input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("TSF9000", error.ToString());
        Assert.Equal(string.Empty, output.ToString());
    }
}
