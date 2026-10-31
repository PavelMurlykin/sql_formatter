using TSqlFormatter.Core.History;

namespace TSqlFormatter.Core.Tests;

public sealed class EditorHistoryLifecycleTests
{
    [Fact]
    public void Untitled_sql_and_discarded_edits_are_retained_until_close()
    {
        var session = new EditorHistorySession();
        session.Remember(1, "SQLQuery1.sql", "SELECT 1;", true, true, 1000);
        session.Remember(1, "SQLQuery1.sql", "SELECT N'unsaved';", true, true, 1000, "test-server", "test-db");
        var closing = session.Take(1, "close", true);
        Assert.NotNull(closing);
        Assert.Equal("SELECT N'unsaved';", closing.Content);
        Assert.Equal("test-server", closing.Server);
        Assert.Equal("test-db", closing.Database);
        Assert.Equal("close", closing.Reason);
        Assert.Null(session.Take(1, "close"));
        // A shell reload after rejecting save cannot mutate the captured copy.
        session.Remember(1, "SQLQuery1.sql", "SELECT 1;", true, true, 1000);
        Assert.Equal("SELECT N'unsaved';", closing.Content);
    }

    [Fact]
    public void Non_sql_disabled_and_oversized_buffers_do_not_leave_stale_contents()
    {
        var session = new EditorHistorySession();
        session.Remember(1, "q.sql", "old", true, true, 10);
        session.Remember(1, "q.sql", "new", true, false, 10);
        Assert.Null(session.Take(1, "close"));
        session.Remember(2, "notes.txt", "SQL-like text", false, true, 100);
        Assert.Null(session.Take(2, "save"));
        session.Remember(3, "q.sql", "short", true, true, 10);
        session.Remember(3, "q.sql", new string('x', 11), true, true, 10);
        Assert.Null(session.Take(3, "close"));
    }

    [Fact]
    public void Context_and_close_reason_round_trip_while_legacy_snapshots_remain_valid()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tsql-history-context-" + Guid.NewGuid().ToString("N"));
        var store = new EditorSnapshotStore(directory);
        Assert.NotNull(store.Save("q.sql", "SELECT 1;", server: "server", database: "db", reason: "close"));
        var item = Assert.Single(store.List());
        Assert.Equal("server", item.Server); Assert.Equal("db", item.Database); Assert.Equal("close", item.Reason);
        Assert.NotNull(store.Save("old.sql", "SELECT 2;"));
        Assert.Null(store.List().Single(s => s.FilePath == "old.sql").Server);
    }

    [Fact]
    public void Age_and_count_rotation_use_snapshot_utc_not_the_copy_time()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tsql-history-retention-" + Guid.NewGuid().ToString("N"));
        var store = new EditorSnapshotStore(directory);
        var now = DateTime.UtcNow;
        store.Save("old.sql", "old", now.AddDays(-40));
        store.Save("boundary.sql", "boundary", now.AddDays(-30));
        store.Save("recent.sql", "recent", now.AddDays(-1));
        store.MaxAgeDays = 30;
        store.Prune(now);
        Assert.Equal(2, store.List().Count);
        Assert.DoesNotContain(store.List(), s => s.FilePath == "old.sql");
        store.MaxSnapshots = 1;
        store.Prune(now);
        Assert.Equal("recent", Assert.Single(store.List()).Content);
    }

    [Fact]
    public void Disabled_history_does_not_prune_existing_files()
    {
        var store = new EditorSnapshotStore(Path.Combine(Path.GetTempPath(), "tsql-history-off-" + Guid.NewGuid().ToString("N")));
        store.Save("q.sql", "keep", DateTime.UtcNow.AddDays(-100));
        store.Enabled = false; store.MaxAgeDays = 1;
        store.Prune();
        Assert.Equal("keep", Assert.Single(store.List()).Content);
    }
}
