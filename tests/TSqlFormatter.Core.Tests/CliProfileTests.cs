using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class CliProfileTests
{
    [Fact]
    public async Task Named_profile_formats_stdin_without_file_discovery()
    {
        var result = await RunAsync(new[] { "--profile", "expanded" }, "select Id, Name from T");

        Assert.Equal(0, result.Code);
        Assert.Equal("SELECT\n    Id,\n    Name\nFROM T", result.Output);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public async Task File_profile_editorconfig_and_json_have_documented_precedence()
    {
        using var tree = new TemporaryTree();
        var path = tree.Write("query.sql", "select Id, Name from T");
        tree.Write(".editorconfig", "root = true\n[*.sql]\nindent_size = 2\n");
        tree.Write(".tsqlformatter.json", """{"version":1,"keywords":{"case":"lower"}}""");

        var result = await RunAsync(new[] { path, "--profile", "Expanded" }, string.Empty);
        var catalog = new FormattingProfileCatalog();
        Assert.True(catalog.TryGet("Expanded", out var expanded));
        var configured = new SqlFormatterConfigurationResolver().ResolveForSqlFile(path, expanded!.Options);
        var core = new ScriptDomSqlFormatter().Format("select Id, Name from T",
            configured.Options!, new FormatRequest());

        Assert.Equal(0, result.Code);
        Assert.Equal("select\n  Id,\n  Name\nfrom T", result.Output);
        Assert.Equal(core.Text, result.Output);
    }

    [Fact]
    public async Task Json_field_overrides_named_profile_without_resetting_other_fields()
    {
        using var tree = new TemporaryTree();
        var path = tree.Write("query.sql", "select Id, Name from T");
        tree.Write(".tsqlformatter.json", """{"version":1,"select":{"columns":"auto"}}""");

        var result = await RunAsync(new[] { "--profile", "Expanded", path }, string.Empty);

        Assert.Equal(0, result.Code);
        Assert.Equal("SELECT Id, Name\nFROM T", result.Output);
    }

    [Fact]
    public async Task Batch_mode_passes_profile_to_each_file()
    {
        using var tree = new TemporaryTree();
        var first = tree.Write("a.sql", "select Id, Name from T");
        var second = tree.Write("b.sql", "select A, B from U");

        var result = await RunAsync(new[] { tree.Root, "--write", "--profile", "Expanded" }, string.Empty);

        Assert.Equal(0, result.Code);
        Assert.Contains("SELECT\n    Id,\n    Name", File.ReadAllText(first));
        Assert.Contains("SELECT\n    A,\n    B", File.ReadAllText(second));
    }

    [Theory]
    [InlineData("--profile", "Unknown", "TSF2000")]
    [InlineData("--profile", "--check", "TSF9000")]
    [InlineData("--profile", "Expanded", "--profile", "Compact", "TSF9000")]
    public async Task Invalid_profile_selection_reports_diagnostic_without_sql_output(
        params string[] arguments)
    {
        var expectedCode = arguments[^1];
        var result = await RunAsync(arguments[..^1], "select 1");

        Assert.Equal(2, result.Code);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains(expectedCode, result.Error);
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(
        string[] args, string source)
    {
        using var input = new StringReader(source);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SqlFormatterCli.RunAsync(args, input, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private sealed class TemporaryTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "tsqlformatter-profile-" + Guid.NewGuid().ToString("N"));

        public TemporaryTree() => Directory.CreateDirectory(Root);

        public string Write(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
