using TSqlFormatter.Cli;

namespace TSqlFormatter.Core.Tests;

public sealed class LargeInputTests
{
    [Fact]
    public async Task Large_but_supported_sql_file_formats_successfully()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-large-{Guid.NewGuid():N}.sql");
        var source = "SELECT '" + new string('x', 1_000_000) + "';";
        await File.WriteAllTextAsync(path, source);
        try
        {
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path }, input, output, error);

            Assert.Equal(0, exitCode);
            Assert.Contains(new string('x', 1000), output.ToString());
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(source, await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Oversized_file_is_rejected_before_parsing_or_writing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-oversize-{Guid.NewGuid():N}.sql");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            stream.SetLength(SqlFormatterCli.MaxInputBytes + 1);
        try
        {
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--write" }, input,
                output, error);

            Assert.Equal(2, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("TSF9001", error.ToString());
            Assert.Equal(SqlFormatterCli.MaxInputBytes + 1, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Oversized_stdin_is_rejected_without_output()
    {
        using var input = new RepeatingReader(SqlFormatterCli.MaxInputCharacters + 1);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(Array.Empty<string>(), input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("TSF9001", error.ToString());
    }

    private sealed class RepeatingReader : TextReader
    {
        private int _remaining;

        public RepeatingReader(int length) => _remaining = length;

        public override ValueTask<int> ReadAsync(Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, _remaining);
            buffer.Span[..count].Fill(' ');
            _remaining -= count;
            return ValueTask.FromResult(count);
        }
    }
}
