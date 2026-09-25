using System.Text;
using TSqlFormatter.Cli;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler handler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += handler;
try
{
    return await SqlFormatterCli.RunAsync(args, Console.In, Console.Out, Console.Error,
        cancellation.Token);
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync("TSF9002: Operation canceled.");
    return 130;
}
finally
{
    Console.CancelKeyPress -= handler;
}
