using TSqlFormatter.Cli;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Tests;

public sealed class EditorConfigTests
{
    [Fact]
    public void Applies_supported_sql_sections_and_json_wins_per_property()
    {
        using var tree = new TemporaryTree();
        var sql = tree.File("sql", "query.sql");
        tree.Write(".editorconfig", """
            root = true
            [*]
            end_of_line = crlf
            insert_final_newline = true
            indent_style = tab
            indent_size = 2
            [*.sql]
            indent_style = space
            """);
        tree.Write("sql", ".tsqlformatter.json", """
            {"version":1,"indent":{"size":3}}
            """);

        var result = new SqlFormatterConfigurationResolver().ResolveForSqlFile(sql);

        Assert.True(result.Succeeded);
        Assert.Equal(DocLineEnding.CrLf, result.Options!.General.LineEnding);
        Assert.True(result.Options.General.FinalNewline);
        Assert.False(result.Options.Indent.UseTabs);
        Assert.Equal(3, result.Options.Indent.Size);
    }

    [Fact]
    public void Nearer_file_overrides_parent_and_unset_restores_baseline()
    {
        using var tree = new TemporaryTree();
        var sql = tree.File("sql", "query.sql");
        tree.Write(".editorconfig", """
            root = true
            [*.sql]
            indent_size = 2
            end_of_line = crlf
            """);
        tree.Write("sql", ".editorconfig", """
            [*.sql]
            indent_size = unset
            end_of_line = lf
            """);
        var baseline = FormattingOptions.Default.With(indent: new IndentOptions(6));

        var result = new SqlFormatterConfigurationResolver().ResolveForSqlFile(sql, baseline);

        Assert.True(result.Succeeded);
        Assert.Equal(6, result.Options!.Indent.Size);
        Assert.Equal(DocLineEnding.Lf, result.Options.General.LineEnding);
    }

    [Fact]
    public void Root_marker_stops_parent_editorconfig_search()
    {
        using var tree = new TemporaryTree();
        var sql = tree.File("nested", "query.sql");
        tree.Write(".editorconfig", """
            [*.sql]
            indent_size = 2
            """);
        tree.Write("nested", ".editorconfig", """
            root = true
            [*.sql]
            end_of_line = crlf
            """);

        var result = new SqlFormatterConfigurationResolver().ResolveForSqlFile(sql);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Options!.Indent.Size);
        Assert.Equal(DocLineEnding.CrLf, result.Options.General.LineEnding);
    }

    [Fact]
    public void Ignores_unsupported_sections_keys_and_values()
    {
        using var tree = new TemporaryTree();
        var sql = tree.File("query.sql");
        tree.Write(".editorconfig", """
            root = true
            [*.cs]
            indent_size = 2
            [*.sql]
            charset = utf-16le
            indent_size = many
            end_of_line = unknown
            """);

        var result = new SqlFormatterConfigurationResolver().ResolveForSqlFile(sql);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Options!.Indent.Size);
        Assert.Equal(DocLineEnding.Lf, result.Options.General.LineEnding);
    }

    [Fact]
    public async Task Cli_file_mode_uses_editorconfig_but_stdin_does_not()
    {
        using var tree = new TemporaryTree();
        var sql = tree.File("query.sql");
        tree.Write("query.sql", "select a from T");
        tree.Write(".editorconfig", """
            root = true
            [*.sql]
            end_of_line = crlf
            insert_final_newline = true
            """);
        using var fileOutput = new StringWriter();
        using var fileError = new StringWriter();

        var exit = await SqlFormatterCli.RunAsync(new[] { sql }, new StringReader(string.Empty),
            fileOutput, fileError);
        using var stdinOutput = new StringWriter();
        var stdinExit = await SqlFormatterCli.RunAsync(Array.Empty<string>(),
            new StringReader("select a from T"), stdinOutput, new StringWriter());

        Assert.Equal(0, exit);
        Assert.Equal("SELECT a\r\nFROM T\r\n", fileOutput.ToString());
        Assert.Equal(string.Empty, fileError.ToString());
        Assert.Equal(0, stdinExit);
        Assert.Equal("SELECT a\nFROM T", stdinOutput.ToString());
    }

    private sealed class TemporaryTree : IDisposable
    {
        public TemporaryTree()
        {
            Root = Path.Combine(Path.GetTempPath(), $"tsqlformatter-editorconfig-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string File(params string[] segments)
        {
            var path = segments.Aggregate(Root, Path.Combine);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        public void Write(params string[] segmentsAndContent)
        {
            var path = File(segmentsAndContent.Take(segmentsAndContent.Length - 1).ToArray());
            System.IO.File.WriteAllText(path, segmentsAndContent[segmentsAndContent.Length - 1]);
        }

        public void Dispose()
        {
            var root = Path.GetFullPath(Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Temporary tree escaped the temp directory.");
            Directory.Delete(root, recursive: true);
        }
    }
}
