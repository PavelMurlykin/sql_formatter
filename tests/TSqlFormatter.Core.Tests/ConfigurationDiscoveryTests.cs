using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;

namespace TSqlFormatter.Core.Tests;

public sealed class ConfigurationDiscoveryTests
{
    [Fact]
    public void Finds_nearest_configuration_upward_from_sql_file()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        var nested = tree.Directory("repo", "nested");
        var deeper = tree.Directory("repo", "nested", "deeper");
        File.WriteAllText(Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName), "{}");
        var nearest = Path.Combine(nested, SqlFormatterConfigurationSerializer.FileName);
        File.WriteAllText(nearest, "{}");

        var found = new SqlFormatterConfigurationDiscovery().FindForFile(Path.Combine(deeper, "query.sql"));

        Assert.Equal(nearest, found);
    }

    [Fact]
    public void Repository_marker_file_stops_search_after_repository_directory()
    {
        using var tree = new TemporaryTree();
        File.WriteAllText(Path.Combine(tree.Root, SqlFormatterConfigurationSerializer.FileName), "{}");
        var repo = tree.Directory("repo");
        File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: elsewhere");
        var nested = tree.Directory("repo", "nested");

        var found = new SqlFormatterConfigurationDiscovery().FindForFile(Path.Combine(nested, "query.sql"));

        Assert.Null(found);
    }

    [Fact]
    public void Without_repository_marker_searches_parent_directories()
    {
        using var tree = new TemporaryTree();
        var expected = Path.Combine(tree.Root, SqlFormatterConfigurationSerializer.FileName);
        File.WriteAllText(expected, "{}");
        var nested = tree.Directory("project", "sql");

        var found = new SqlFormatterConfigurationDiscovery().FindForFile(Path.Combine(nested, "query.sql"));

        Assert.Equal(expected, found);
    }

    [Fact]
    public async Task Cli_applies_discovered_configuration_to_file_output_and_check()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        var nested = tree.Directory("repo", "nested");
        await File.WriteAllTextAsync(Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName),
            """{"version":1,"keywords":{"case":"lower"}}""");
        var path = Path.Combine(nested, "query.sql");
        await File.WriteAllTextAsync(path, "SELECT Id FROM T");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { path }, input, output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal("select Id\nfrom T", output.ToString());
        Assert.Equal(string.Empty, error.ToString());

        await File.WriteAllTextAsync(path, "select Id\nfrom T");
        using var checkOutput = new StringWriter();
        var checkCode = await SqlFormatterCli.RunAsync(new[] { path, "--check" }, input,
            checkOutput, error);
        Assert.Equal(0, checkCode);
        Assert.Equal(string.Empty, checkOutput.ToString());
    }

    [Fact]
    public async Task Invalid_discovered_configuration_blocks_write()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        var path = Path.Combine(repo, "query.sql");
        await File.WriteAllTextAsync(path, "select Id from T");
        await File.WriteAllTextAsync(Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName),
            """{"version":1,"keywords":{"case":"unknown"}}""");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SqlFormatterCli.RunAsync(new[] { path, "--write" }, input,
            output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("TSF2000", error.ToString());
        Assert.Contains(SqlFormatterConfigurationSerializer.FileName, error.ToString());
        Assert.Equal("select Id from T", await File.ReadAllTextAsync(path));
    }

    private sealed class TemporaryTree : IDisposable
    {
        public TemporaryTree()
        {
            Root = Path.Combine(Path.GetTempPath(), $"tsqlformatter-discovery-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Directory(params string[] segments)
        {
            var path = segments.Aggregate(Root, Path.Combine);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            var root = Path.GetFullPath(Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Temporary tree escaped the temp directory.");
            System.IO.Directory.Delete(root, recursive: true);
        }
    }
}
