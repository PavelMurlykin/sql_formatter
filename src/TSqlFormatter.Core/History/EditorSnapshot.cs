namespace TSqlFormatter.Core.History;

/// <summary>One locally stored snapshot of an editor document.</summary>
public sealed class EditorSnapshot
{
    public EditorSnapshot(string filePath, DateTime timestampUtc, string content,
        string? server = null, string? database = null, string? reason = null)
    {
        if (filePath is null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        FilePath = filePath;
        TimestampUtc = timestampUtc;
        Content = content;
        Server = server; Database = database; Reason = reason;
    }

    /// <summary>Original document path, or an empty string for untitled buffers.</summary>
    public string FilePath { get; }

    public DateTime TimestampUtc { get; }

    public string Content { get; }
    public string? Server { get; }
    public string? Database { get; }
    public string? Reason { get; }

    public string DisplayName => FilePath.Length == 0 ? "<untitled>" : System.IO.Path.GetFileName(FilePath);
}
