using TSqlFormatter.Core.History;

namespace TSqlFormatter.Core.Tests;

public sealed class EditorSnapshotStoreTests
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"tsqlformatter-history-{Guid.NewGuid():N}");

    private EditorSnapshotStore CreateStore() => new(directory);

    [Fact]
    public void Save_writes_local_json_and_List_returns_it_first()
    {
        var store = CreateStore();
        var savedAt = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        var path = store.Save(@"C:\work\query.sql", "SELECT 1;", savedAt);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Contains("tsqlformatter-history-", path, StringComparison.Ordinal);
        var snapshot = Assert.Single(store.List());
        Assert.Equal(@"C:\work\query.sql", snapshot.FilePath);
        Assert.Equal("query.sql", snapshot.DisplayName);
        Assert.Equal("SELECT 1;", snapshot.Content);
        Assert.Equal(savedAt, snapshot.TimestampUtc);
    }

    [Fact]
    public void Save_orders_newest_first_and_handles_unsaved_buffers()
    {
        var store = CreateStore();

        store.Save(@"C:\a.sql", "A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        store.Save(string.Empty, "B", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var snapshots = store.List();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal("B", snapshots[0].Content);
        Assert.Equal("<untitled>", snapshots[0].DisplayName);
        Assert.Equal("A", snapshots[1].Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Save_keeps_both_versions_of_the_same_file_within_one_second(int milliseconds)
    {
        var store = CreateStore();
        var timestamp = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        var first = store.Save(@"C:\work\query.sql", "SELECT 1;", timestamp);
        var second = store.Save(@"C:\work\query.sql", "SELECT 2;", timestamp.AddMilliseconds(milliseconds));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.Contains("SELECT 1;", File.ReadAllText(first), StringComparison.Ordinal);
        Assert.Contains("SELECT 2;", File.ReadAllText(second), StringComparison.Ordinal);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void Save_does_not_confuse_files_with_the_same_truncated_path()
    {
        var store = CreateStore();
        var filename = new string('q', 45) + ".sql";
        var timestamp = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        var first = store.Save(@"C:\first\" + filename, "FIRST", timestamp);
        var second = store.Save(@"C:\second\" + filename, "SECOND", timestamp);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void List_reads_snapshots_with_the_legacy_filename()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "20261025-120000-query.sql.json"),
            "{\"version\":1,\"filePath\":\"query.sql\",\"timestampUtc\":\"2026-10-25T12:00:00Z\",\"content\":\"SELECT 1;\"}");

        Assert.Equal("SELECT 1;", Assert.Single(CreateStore().List()).Content);
    }

    [Fact]
    public void Save_respects_enabled_switch_and_size_limit()
    {
        var store = CreateStore();

        store.Enabled = false;
        Assert.Null(store.Save(@"C:\a.sql", "SELECT 1;"));
        Assert.Empty(store.List());

        store.Enabled = true;
        store.MaxSnapshotCharacters = 5;
        Assert.Null(store.Save(@"C:\a.sql", "SELECT 1;"));
        Assert.Null(store.Save(@"C:\a.sql", "123456"));

        store.MaxSnapshotCharacters = 1_000_000;
        Assert.NotNull(store.Save(@"C:\a.sql", "short"));
        Assert.Single(store.List());
    }

    [Fact]
    public void Prune_keeps_only_newest_snapshots()
    {
        var store = CreateStore();
        store.MaxSnapshots = 3;

        for (var index = 0; index < 5; index++)
        {
            store.Save($@"C:\f{index}.sql", $"content {index}",
                new DateTime(2026, 1, index + 1, 0, 0, 0, DateTimeKind.Utc));
        }

        var snapshots = store.List();
        Assert.Equal(3, snapshots.Count);
        Assert.Equal("content 4", snapshots[0].Content);
        Assert.Equal("content 2", snapshots[2].Content);
    }

    [Fact]
    public void Search_matches_name_path_and_content_case_insensitively()
    {
        var store = CreateStore();
        store.Save(@"C:\work\Orders.sql", "SELECT * FROM dbo.Orders;", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        store.Save(@"C:\work\report.sql", "-- audit trail", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        store.Save(@"C:\work\misc.sql", "SELECT 1;", new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(3, store.Search("").Count);
        Assert.Single(store.Search("orders"));     // name
        Assert.Single(store.Search("WORK\\R"));    // path
        Assert.Single(store.Search("audit"));      // content
        Assert.Single(store.Search("SELECT *"));
    }

    [Fact]
    public void Missing_or_broken_directory_yields_empty_list()
    {
        Assert.Empty(CreateStore().List());

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");
        Assert.Empty(CreateStore().List());
    }

    [Fact]
    public void Null_arguments_throw()
    {
        var store = CreateStore();
        Assert.Throws<ArgumentNullException>(() => store.Save(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => store.Save(@"C:\a.sql", null!));
        Assert.Throws<ArgumentNullException>(() => store.Search(null!));
    }
}
