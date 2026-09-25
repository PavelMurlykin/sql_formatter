using TSqlFormatter.Cli;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class CrashCorpusTests
{
    [Fact]
    public async Task Every_corpus_case_is_handled_without_a_crash_or_partial_strict_output()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "CrashCorpus");
        var files = Directory.GetFiles(directory, "*.sql").OrderBy(path => path,
            StringComparer.Ordinal).ToArray();
        Assert.True(files.Length >= 6, "The crash regression corpus must be copied to test output.");

        foreach (var path in files)
        {
            var source = await File.ReadAllTextAsync(path);
            try
            {
                var parsed = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto);
                var formatter = new ScriptDomSqlFormatter();
                var strict = formatter.Format(source, FormattingOptions.Default, new FormatRequest());
                var safe = formatter.Format(source, FormattingOptions.Default,
                    new FormatRequest(parseFailureBehavior: ParseFailureBehavior.Safe));

                Assert.Equal(parsed.ParseSucceeded, strict.ParseSucceeded);
                if (!parsed.ParseSucceeded)
                {
                    Assert.Equal(source, strict.Text);
                    Assert.False(strict.Changed);
                    Assert.Empty(strict.Edits);
                    Assert.Contains(strict.Diagnostics, diagnostic => diagnostic.Code == "TSF1000");
                    Assert.False(safe.ParseSucceeded);
                }

                using var input = new StringReader(source);
                using var output = new StringWriter();
                using var error = new StringWriter();
                var exitCode = await SqlFormatterCli.RunAsync(Array.Empty<string>(), input, output, error);
                var expectedCode = strict.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == FormatterDiagnosticSeverity.Error) ? 2 : 0;
                Assert.Equal(expectedCode, exitCode);
                if (expectedCode == 2) Assert.Equal(string.Empty, output.ToString());
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Crash corpus case failed: {Path.GetFileName(path)}",
                    exception);
            }
        }
    }
}
