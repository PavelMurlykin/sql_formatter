using System.Diagnostics;
using System.Text;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Benchmarks;

/// <summary>Reproducible one-pass measurements of complete stored-procedure formatting.</summary>
internal static class LargeProcedureValidation
{
    public static void Run()
    {
        var formatter = new ScriptDomSqlFormatter();
        formatter.Format(CreateProcedure(10), FormattingOptions.Default, new FormatRequest());
        Console.WriteLine("Assignments\tCharacters\tElapsedMs\tAllocatedMiB");
        foreach (var assignments in new[] { 100, 500, 1000 })
        {
            var source = CreateProcedure(assignments);
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            var result = formatter.Format(source, FormattingOptions.Default, new FormatRequest());
            stopwatch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (!result.ParseSucceeded || result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == FormatterDiagnosticSeverity.Error))
                throw new InvalidOperationException(
                    $"Procedure with {assignments} assignments failed to format safely.");

            Console.WriteLine($"{assignments}\t{source.Length}\t" +
                $"{stopwatch.Elapsed.TotalMilliseconds:F1}\t{allocated / 1048576.0:F2}");
        }
    }

    private static string CreateProcedure(int assignments)
    {
        var sql = new StringBuilder("CREATE PROCEDURE dbo.BenchmarkProcedure AS\nBEGIN\n");
        sql.Append("DECLARE @value int;\nSET @value = 0;\n");
        for (var index = 0; index < assignments; index++)
            sql.Append("SET @value = @value + 1;\n");
        sql.Append("SELECT @value AS Result;\nEND;\n");
        return sql.ToString();
    }
}
