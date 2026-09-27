using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class ConfigurationDiscoveryTests
{
    [Fact]
    public void Save_formatting_policy_is_off_by_default_and_requires_sql_document()
    {
        using var tree = new TemporaryTree();
        var sql = Path.Combine(tree.Root, "query.sql");
        var policy = new SqlSaveFormattingPolicy();

        Assert.False(policy.ShouldFormat(sql, SqlSaveFormattingMode.Off));
        Assert.True(policy.ShouldFormat(sql, SqlSaveFormattingMode.CurrentDocument));
        Assert.False(policy.ShouldFormat(Path.Combine(tree.Root, "notes.txt"),
            SqlSaveFormattingMode.CurrentDocument));
    }

    [Fact]
    public void Save_formatting_policy_can_require_nearest_project_config()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        var sql = Path.Combine(repo, "query.sql");
        var policy = new SqlSaveFormattingPolicy();

        Assert.False(policy.ShouldFormat(sql, SqlSaveFormattingMode.OnlyWhenProjectConfigExists));
        File.WriteAllText(Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName),
            """{"version":1}""");
        Assert.True(policy.ShouldFormat(sql, SqlSaveFormattingMode.OnlyWhenProjectConfigExists));
    }
    [Fact]
    public void Profile_exchange_exports_and_imports_current_options()
    {
        using var tree = new TemporaryTree();
        var path = Path.Combine(tree.Root, "profile.json");
        var options = FormattingOptions.Default.With(
            general: new GeneralOptions(maxLineWidth: 72),
            joins: new JoinOptions(false, true));
        var exchange = new SqlFormatterProfileExchange();

        exchange.Export(path, options);
        var imported = exchange.Import(path);

        Assert.True(imported.Succeeded);
        Assert.Equal(72, imported.Options!.General.MaxLineWidth);
        Assert.False(imported.Options.Joins.ClauseNewLine);
    }

    [Fact]
    public void Profile_exchange_rejects_invalid_content_without_options()
    {
        using var tree = new TemporaryTree();
        var path = Path.Combine(tree.Root, "profile.json");
        File.WriteAllText(path, """{"version":1,"keywords":{"case":"invalid"}}""");

        var imported = new SqlFormatterProfileExchange().Import(path);

        Assert.False(imported.Succeeded);
        Assert.Null(imported.Options);
        Assert.Contains(imported.Diagnostics, d => d.Code == "TSF2000" && d.Message.Contains(path));
    }

    [Fact]
    public void Profile_exchange_rejects_oversized_file()
    {
        using var tree = new TemporaryTree();
        var path = Path.Combine(tree.Root, "profile.json");
        using (var stream = File.Create(path)) stream.SetLength(SqlFormatterProfileExchange.MaxProfileBytes + 1);

        var imported = new SqlFormatterProfileExchange().Import(path);

        Assert.False(imported.Succeeded);
        Assert.Equal("TSF9001", Assert.Single(imported.Diagnostics).Code);
    }
    [Fact]
    public void Resolver_loads_nearest_file_for_editor_path()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        var nested = tree.Directory("repo", "nested");
        File.WriteAllText(Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName),
            """{"version":1,"keywords":{"case":"upper"}}""");
        File.WriteAllText(Path.Combine(nested, SqlFormatterConfigurationSerializer.FileName),
            """{"version":1,"keywords":{"case":"lower"}}""");

        var result = new SqlFormatterConfigurationResolver()
            .ResolveForSqlFile(Path.Combine(nested, "query.sql"));

        Assert.True(result.Succeeded);
        Assert.Equal(KeywordCase.Lower, result.Options!.Keywords.Case);
    }

    [Fact]
    public void Invalid_editor_configuration_returns_diagnostic_without_options()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        var path = Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName);
        File.WriteAllText(path, """{"version":1,"keywords":{"case":"bad"}}""");

        var result = new SqlFormatterConfigurationResolver()
            .ResolveForSqlFile(Path.Combine(repo, "query.sql"));

        Assert.False(result.Succeeded);
        Assert.Null(result.Options);
        Assert.Contains(result.Diagnostics, d => d.Code == "TSF2000" && d.Message.Contains(path));
    }

    [Fact]
    public void Editor_configuration_without_file_uses_defaults()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");

        var result = new SqlFormatterConfigurationResolver()
            .ResolveForSqlFile(Path.Combine(repo, "query.sql"));

        Assert.True(result.Succeeded);
        Assert.Equal(KeywordCase.Upper, result.Options!.Keywords.Case);
    }

    [Fact]
    public void Editor_configuration_uses_ide_baseline_when_no_file_exists()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        var baseline = FormattingOptions.Default.With(keywords: new KeywordOptions(KeywordCase.Lower));

        var result = new SqlFormatterConfigurationResolver()
            .ResolveForSqlFile(Path.Combine(repo, "query.sql"), baseline);

        Assert.True(result.Succeeded);
        Assert.Equal(KeywordCase.Lower, result.Options!.Keywords.Case);
    }

    [Fact]
    public void Editor_configuration_overlays_ide_baseline_by_property()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        File.WriteAllText(Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName),
            """{"version":1,"keywords":{"case":"upper"}}""");
        var baseline = FormattingOptions.Default.With(
            general: new GeneralOptions(maxLineWidth: 72),
            keywords: new KeywordOptions(KeywordCase.Lower));

        var result = new SqlFormatterConfigurationResolver()
            .ResolveForSqlFile(Path.Combine(repo, "query.sql"), baseline);

        Assert.True(result.Succeeded);
        Assert.Equal(KeywordCase.Upper, result.Options!.Keywords.Case);
        Assert.Equal(72, result.Options.General.MaxLineWidth);
    }

    [Fact]
    public void Project_configuration_overlays_selected_profile_baseline()
    {
        using var tree = new TemporaryTree();
        var repo = tree.Directory("repo");
        tree.Directory("repo", ".git");
        File.WriteAllText(Path.Combine(repo, SqlFormatterConfigurationSerializer.FileName),
            """{"version":1,"keywords":{"case":"lower"}}""");
        var catalog = new FormattingProfileCatalog();
        Assert.True(catalog.TryGet("Expanded", out var expanded));

        var result = new SqlFormatterConfigurationResolver(catalog)
            .ResolveForSqlFile(Path.Combine(repo, "query.sql"), expanded!.Options);

        Assert.True(result.Succeeded);
        Assert.Equal(80, result.Options!.General.MaxLineWidth);
        Assert.Equal(SelectColumnLayout.OnePerLine, result.Options.Select.ColumnLayout);
        Assert.Equal(KeywordCase.Lower, result.Options.Keywords.Case);
    }

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
