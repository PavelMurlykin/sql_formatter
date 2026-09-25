using TSqlFormatter.Cli;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Tests;

public sealed class CancellationBehaviorTests
{
    [Fact]
    public void Formatter_observes_a_pre_canceled_token()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => new ScriptDomSqlFormatter().Format(
            "select Id from T", FormattingOptions.Default, new FormatRequest(),
            cancellation.Token));
    }

    [Fact]
    public void Renderer_observes_cancellation_while_processing_a_large_literal()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new TextDoc(new string('x', 10_000_000));
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(1));

        Assert.Throws<OperationCanceledException>(() => new DocRenderer().Render(
            document, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Cli_cancellation_while_reading_stdin_does_not_emit_sql()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        using var input = new BlockingReader();
        using var output = new StringWriter();
        using var error = new StringWriter();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SqlFormatterCli.RunAsync(
            Array.Empty<string>(), input, output, error, cancellation.Token));

        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task Cli_cancellation_does_not_replace_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tsqlformatter-cancel-{Guid.NewGuid():N}.sql");
        const string source = "select Id from T";
        await File.WriteAllTextAsync(path, source);
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SqlFormatterCli.RunAsync(
                new[] { path, "--write" }, input, output, error, cancellation.Token));

            Assert.Equal(source, await File.ReadAllTextAsync(path));
            Assert.Equal(string.Empty, output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class BlockingReader : TextReader
    {
        public override async ValueTask<int> ReadAsync(Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
