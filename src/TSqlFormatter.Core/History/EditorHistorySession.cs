namespace TSqlFormatter.Core.History;

/// <summary>Retains the last SQL buffer contents until close, including untitled/discarded buffers.</summary>
public sealed class EditorHistorySession
{
    private readonly Dictionary<uint, EditorSnapshot> documents = new();

    public void Remember(uint id, string filePath, string content, bool isSql, bool enabled,
        int maxCharacters, string? server = null, string? database = null)
    {
        if (!enabled || !isSql || content.Length > maxCharacters) { Forget(id); return; }
        documents[id] = new EditorSnapshot(filePath, DateTime.UtcNow, content, server, database);
    }

    public EditorSnapshot? Take(uint id, string reason, bool remove = false)
    {
        if (!documents.TryGetValue(id, out var snapshot)) return null;
        if (remove) documents.Remove(id);
        return new(snapshot.FilePath, DateTime.UtcNow, snapshot.Content, snapshot.Server, snapshot.Database, reason);
    }

    public void Forget(uint id) => documents.Remove(id);
    public void Clear() => documents.Clear();
}
