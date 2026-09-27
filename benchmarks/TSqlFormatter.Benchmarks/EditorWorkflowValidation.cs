using System.Diagnostics;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Benchmarks;

/// <summary>Quick, non-gating editor-sized measurements plus cancellation smoke test.</summary>
internal static class EditorWorkflowValidation
{
    public static void Run()
    {
        var formatter = new ScriptDomSqlFormatter();
        formatter.Format("select Id from dbo.Items;", FormattingOptions.Default, new FormatRequest());
        Console.WriteLine("Statements\tCharacters\tElapsedMs\tAllocatedMiB");
        foreach (int count in new[] { 10, 100, 1000 })
        {
            string source = string.Concat(Enumerable.Repeat(
                "select Id, Name from dbo.Items where Id = 1 and Name <> 'x';\n", count));
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            FormatResult result = formatter.Format(source, FormattingOptions.Default, new FormatRequest());
            stopwatch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (!result.ParseSucceeded)
                throw new InvalidOperationException($"Synthetic workload with {count} statements failed to parse.");
            Console.WriteLine($"{count}\t{source.Length}\t{stopwatch.Elapsed.TotalMilliseconds:F1}\t{allocated / 1048576.0:F2}");
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            formatter.Format("select Id from dbo.Items;", FormattingOptions.Default,
                new FormatRequest(), cancellation.Token);
            throw new InvalidOperationException("Pre-canceled formatting unexpectedly succeeded.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Pre-canceled formatter request: canceled before editing.");
        }
    }
}
