using System.Text;
using TSqlFormatter.Cli;

namespace TSqlFormatter.Core.Tests;

public sealed class CliBatchTests
{
    [Fact]
    public async Task Directory_check_recurses_filters_sql_and_applies_relative_exclusions()
    {
        using var tree = new TemporaryTree();
        var first = tree.Write("z.sql", "select 1");
        var second = tree.Write("nested/a.SQL", "select 2");
        tree.Write("nested/notes.txt", "select 3");
        tree.Write("ignored/skip.sql", "select 4");

        var result = await RunAsync(tree.Root, "--check", "--exclude", "ignored");

        Assert.Equal(1, result.Code);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal("select 1", File.ReadAllText(first));
        Assert.Equal("select 2", File.ReadAllText(second));
        Assert.Contains(first, result.Error);
        Assert.Contains(second, result.Error);
        Assert.DoesNotContain("skip.sql", result.Error);
        Assert.True(result.Error.IndexOf(second, StringComparison.Ordinal) <
                    result.Error.IndexOf(first, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Directory_write_preserves_bom_and_skips_exact_file_exclusion()
    {
        using var tree = new TemporaryTree();
        var first = tree.Write("one.sql", "select 1", bom: true);
        var second = tree.Write("nested/two.SQL", "select 2");
        var skipped = tree.Write("nested/skip.sql", "select 3");

        var result = await RunAsync(tree.Root, "--write", "--exclude", "nested/skip.sql");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal("SELECT 1", File.ReadAllText(first));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(first)[..3]);
        Assert.Equal("SELECT 2", File.ReadAllText(second));
        Assert.Equal("select 3", File.ReadAllText(skipped));
    }

    [Fact]
    public async Task Batch_check_reports_changed_files_and_errors_with_error_precedence()
    {
        using var tree = new TemporaryTree();
        var valid = tree.Write("a.sql", "select 1");
        var invalid = tree.Write("b.sql", "select from");

        var result = await RunAsync(valid, invalid, "--check");

        Assert.Equal(2, result.Code);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains($"Would reformat: {valid}", result.Error);
        Assert.Contains($"{invalid}: TSF1000", result.Error);
        Assert.Equal("select 1", File.ReadAllText(valid));
        Assert.Equal("select from", File.ReadAllText(invalid));
    }

    [Fact]
    public async Task Batch_write_continues_after_invalid_file_without_replacing_it()
    {
        using var tree = new TemporaryTree();
        var invalid = tree.Write("a.sql", "select from");
        var valid = tree.Write("b.sql", "select 1");

        var result = await RunAsync(invalid, valid, "--write");

        Assert.Equal(2, result.Code);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains($"{invalid}: TSF1000", result.Error);
        Assert.Equal("select from", File.ReadAllText(invalid));
        Assert.Equal("SELECT 1", File.ReadAllText(valid));
    }

    [Fact]
    public async Task Overlapping_operands_are_processed_only_once()
    {
        using var tree = new TemporaryTree();
        var file = tree.Write("a.sql", "select 1");

        var result = await RunAsync(tree.Root, file, "--check");

        Assert.Equal(1, result.Code);
        Assert.Equal(1, result.Error.Split("Would reformat:", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Batch_resolves_configuration_for_each_file()
    {
        using var tree = new TemporaryTree();
        tree.Write(".tsqlformatter.json", """{"version":1,"keywords":{"case":"lower"}}""");
        tree.Write("nested/.tsqlformatter.json", """{"version":1,"keywords":{"case":"upper"}}""");
        var rootFile = tree.Write("root.sql", "select 1");
        var nestedFile = tree.Write("nested/query.sql", "select 2");

        var result = await RunAsync(tree.Root, "--write");

        Assert.Equal(0, result.Code);
        Assert.Equal("select 1", File.ReadAllText(rootFile));
        Assert.Equal("SELECT 2", File.ReadAllText(nestedFile));
    }

    [Fact]
    public async Task Empty_directory_or_ambiguous_batch_arguments_return_two()
    {
        using var tree = new TemporaryTree();
        var file = tree.Write("notes.txt", "select 1");

        var empty = await RunAsync(tree.Root, "--check");
        var noMode = await RunAsync(file, file);
        var badExclusion = await RunAsync(tree.Root, "--check", "--exclude", "../escape");

        Assert.Equal(2, empty.Code);
        Assert.Contains("No SQL files", empty.Error);
        Assert.Equal(2, noMode.Code);
        Assert.Contains("Batch mode", noMode.Error);
        Assert.Equal(2, badExclusion.Code);
        Assert.Contains("Batch mode", badExclusion.Error);
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SqlFormatterCli.RunAsync(args, new StringReader(string.Empty), output, error);
        return (code, output.ToString(), error.ToString());
    }

    private sealed class TemporaryTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            $"tsqlformatter-batch-{Guid.NewGuid():N}");

        public TemporaryTree() => Directory.CreateDirectory(Root);

        public string Write(string relativePath, string text, bool bom = false)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(bom));
            return path;
        }

        public void Dispose()
        {
            var root = Path.GetFullPath(Root);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
                Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test fixture escaped the temporary directory.");
            Directory.Delete(root, recursive: true);
        }
    }
}
