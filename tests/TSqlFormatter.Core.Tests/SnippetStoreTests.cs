using TSqlFormatter.Core.Snippets;

namespace TSqlFormatter.Core.Tests;

public sealed class SnippetStoreTests
{
    [Fact]
    public void BuiltIn_contains_five_unique_snippets_with_shortcuts()
    {
        var snippets = SnippetStore.BuiltIn;

        Assert.Equal(5, snippets.Count);
        Assert.All(snippets, snippet => Assert.False(string.IsNullOrWhiteSpace(snippet.Shortcut)));
        Assert.Equal(snippets.Count, snippets.Select(snippet => snippet.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(snippets, snippet => snippet.Shortcut == "proc" && snippet.Body.Contains("$ProcedureName$"));
        Assert.Contains(snippets, snippet => snippet.Shortcut == "try" && snippet.Body.Contains("BEGIN TRY"));
        Assert.Contains(snippets, snippet => snippet.Shortcut == "tran" && snippet.Body.Contains("ROLLBACK TRANSACTION"));
        Assert.Contains(snippets, snippet => snippet.Shortcut == "select" && snippet.Body.Contains("INNER JOIN"));
        Assert.Contains(snippets, snippet => snippet.Shortcut == "insert" && snippet.Body.Contains("VALUES"));
    }

    [Fact]
    public void Load_creates_missing_user_directory_and_returns_built_in_snippets()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tsqlformatter-snippets-{Guid.NewGuid():N}");
        try
        {
            var store = new SnippetStore(directory);

            var snippets = store.Load();

            Assert.True(Directory.Exists(directory));
            Assert.Equal(SnippetStore.BuiltIn.Count, snippets.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_merges_user_snippets_and_user_overrides_built_in()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tsqlformatter-snippets-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "mine.json"),
                "{\"name\":\"My audit query\",\"shortcut\":\"audit\",\"body\":\"SELECT $columns$ FROM dbo.Audit\"}");
            File.WriteAllText(Path.Combine(directory, "override.json"),
                "{\"name\":\"Replaced try\",\"shortcut\":\"try\",\"body\":\"SELECT 1\"}");

            var snippets = new SnippetStore(directory).Load();

            Assert.Equal(SnippetStore.BuiltIn.Count + 1, snippets.Count);
            Assert.Contains(snippets, snippet => snippet.Shortcut == "audit");
            Assert.Contains(snippets, snippet => snippet.Shortcut == "try" && snippet.Body == "SELECT 1");
            Assert.DoesNotContain(snippets, snippet => snippet.Shortcut == "try" && snippet.Body.Contains("BEGIN TRY"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_skips_invalid_user_files_and_keeps_built_in()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tsqlformatter-snippets-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");
            File.WriteAllText(Path.Combine(directory, "no-name.json"), "{\"shortcut\":\"x\",\"body\":\"SELECT 1\"}");

            var snippets = new SnippetStore(directory).Load();

            Assert.Equal(SnippetStore.BuiltIn.Count, snippets.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("{\"name\":\"One\",\"body\":\"SELECT 1\"}", 1)]
    [InlineData("[{\"name\":\"One\",\"body\":\"SELECT 1\"},{\"name\":\"Two\",\"shortcut\":\"tw\",\"description\":\"d\",\"body\":\"SELECT 2\"}]", 2)]
    public void SnippetJson_parses_single_object_and_array(string json, int expected)
    {
        Assert.True(SnippetJson.TryParse(json, out var snippets, out var error));
        Assert.Null(error);
        Assert.Equal(expected, snippets.Count);
        Assert.All(snippets, snippet => Assert.False(string.IsNullOrWhiteSpace(snippet.Body)));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"name\":\"\",\"body\":\"SELECT 1\"}")]
    [InlineData("{\"name\":\"x\"}")]
    [InlineData("{\"name\":\"x\",\"body\":3}")]
    [InlineData("{\"name\":\"x\",\"shortcut\":\"a b\",\"body\":\"SELECT 1\"}")]
    [InlineData("[]")]
    [InlineData("42")]
    public void SnippetJson_rejects_invalid_documents(string json)
    {
        Assert.False(SnippetJson.TryParse(json, out var snippets, out var error));
        Assert.Empty(snippets);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Snippet_trims_name_and_shortcut()
    {
        var snippet = new SqlSnippet("  Query  ", " q ", "SELECT 1");

        Assert.Equal("Query", snippet.Name);
        Assert.Equal("q", snippet.Shortcut);
        Assert.Equal("q", snippet.Key);
    }

    [Fact]
    public void Snippet_without_shortcut_uses_name_as_key()
    {
        var snippet = new SqlSnippet("Query", "", "SELECT 1");

        Assert.Equal("Query", snippet.Key);
    }

    [Theory]
    [InlineData("  ", "trims to an empty shortcut")]
    [InlineData("a b", "keeps inner whitespace")]
    public void Snippet_rejects_whitespace_shortcut(string shortcut, string reason)
    {
        _ = reason;
        if (shortcut.Trim().Length == 0)
        {
            var trimmed = new SqlSnippet("q", shortcut, "SELECT 1");
            Assert.Equal(string.Empty, trimmed.Shortcut);
            return;
        }

        Assert.Throws<ArgumentException>(() => new SqlSnippet("q", shortcut, "SELECT 1"));
    }
}

public sealed class SnippetExpanderTests
{
    [Fact]
    public void Expand_without_parameters_returns_unchanged_text()
    {
        var expanded = SnippetExpander.Expand("SELECT 1;");

        Assert.Equal("SELECT 1;", expanded.Text);
        Assert.Empty(expanded.Parameters);
    }

    [Fact]
    public void Expand_replaces_parameter_with_name_text_and_reports_span()
    {
        var expanded = SnippetExpander.Expand("dbo.$Table$;");

        Assert.Equal("dbo.Table;", expanded.Text);
        var span = Assert.Single(expanded.Parameters);
        Assert.Equal(4, span.StartOffset);
        Assert.Equal(5, span.Length);
    }

    [Fact]
    public void Expand_reports_repeated_parameters_in_order()
    {
        var expanded = SnippetExpander.Expand("$a$ + $b$ + $a$");

        Assert.Equal("a + b + a", expanded.Text);
        Assert.Equal(3, expanded.Parameters.Count);
        Assert.Equal(0, expanded.Parameters[0].StartOffset);
        Assert.Equal(4, expanded.Parameters[1].StartOffset);
        Assert.Equal(8, expanded.Parameters[2].StartOffset);
    }

    [Theory]
    [InlineData("SELECT 1 $", "SELECT 1 $")]
    [InlineData("SELECT $$", "SELECT $$")]
    [InlineData("SELECT $ x $", "SELECT $ x $")]
    [InlineData("SELECT $a b$", "SELECT $a b$")]
    public void Expand_keeps_lone_dollars_literal(string body, string expected)
    {
        var expanded = SnippetExpander.Expand(body);

        Assert.Equal(expected, expanded.Text);
        Assert.Empty(expanded.Parameters);
    }

    [Fact]
    public void Expand_accepts_multiline_body()
    {
        var expanded = SnippetExpander.Expand("INSERT INTO dbo.$Table$\n    ($columns$)\nVALUES ($values$);");

        Assert.Equal("INSERT INTO dbo.Table\n    (columns)\nVALUES (values);", expanded.Text);
        Assert.Equal(3, expanded.Parameters.Count);
    }
}
