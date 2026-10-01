using System.Diagnostics;
using System.Globalization;
using System.Text;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Benchmarks;

/// <summary>Warm synchronous end-to-end time and allocated bytes; not a UI responsiveness or peak-memory claim.</summary>
internal static class ParityWorkflowValidation
{
    public static void Run()
    {
        var formatter = new ScriptDomSqlFormatter();
        var profiles = new[] { new FormattingProfile("DefaultV1", "Default v1", FormattingOptions.Default) }
            .Concat(NativeFormattingPresets.Profiles).ToArray();
        Console.WriteLine("Profile\tAssignments\tCharacters\tElapsedMs\tAllocatedMiB\tStable");
        foreach (var profile in profiles)
        {
            formatter.Format(Source(10), profile.Options, new FormatRequest());
            foreach (int count in new[] { 100, 300, 1000 })
            {
                string sql = Source(count);
                GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                var result = formatter.Format(sql, profile.Options, new FormatRequest());
                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (!result.ParseSucceeded || result.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error))
                    throw new InvalidOperationException("Invalid benchmark result: " + profile.Id);
                bool stable = result.Text == formatter.Format(result.Text, profile.Options, new FormatRequest()).Text;
                if (!stable) throw new InvalidOperationException("Unstable benchmark result: " + profile.Id);
                Console.WriteLine(string.Join("\t", profile.Id, count, sql.Length,
                    stopwatch.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture),
                    (allocated / 1048576.0).ToString("F2", CultureInfo.InvariantCulture), stable));
                // Generous reproducible editor-size guard, not a timing-sensitive unit test.
                if (sql.Length <= 8192 && (stopwatch.Elapsed > TimeSpan.FromSeconds(2) || allocated > 128L * 1048576))
                    throw new InvalidOperationException("Editor-size parity budget exceeded: " + profile.Id);
            }
        }
    }

    private static string Source(int count)
    {
        var sql = new StringBuilder("CREATE PROCEDURE dbo.ParityBenchmark @a int, @b int AS BEGIN DECLARE @v int; SET @v = 0;\n");
        for (int i = 0; i < count; i++) sql.Append("SET @v = @v + 1;\n");
        return sql.Append("SELECT @v, @a, @b; END;\n").ToString();
    }
}
