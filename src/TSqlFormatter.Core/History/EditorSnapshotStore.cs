using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TSqlFormatter.Core.History;

/// <summary>
/// Local store of editor snapshots in a per-user directory (default
/// %USERPROFILE%\.tsqlformatter\history). One JSON file per snapshot; rotation keeps
/// the newest <see cref="MaxSnapshots"/> files. Everything is strictly local.
/// </summary>
public sealed class EditorSnapshotStore
{
    private readonly string directory;

    public EditorSnapshotStore(string? directory = null)
    {
        this.directory = directory ?? DefaultDirectory;
    }

    public static string DefaultDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".tsqlformatter", "history");

    /// <summary>When false, Save becomes a no-op; existing files stay on disk.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Rotation limit: snapshots older than the newest MaxSnapshots are deleted.</summary>
    public int MaxSnapshots { get; set; } = 100;

    /// <summary>Snapshots above this content length are not stored.</summary>
    public int MaxSnapshotCharacters { get; set; } = 1_000_000;

    /// <summary>Stores a snapshot; returns its file path, or null when disabled or too large.</summary>
    public string? Save(string filePath, string content, DateTime? timestampUtc = null)
    {
        if (filePath is null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (!Enabled || content.Length > MaxSnapshotCharacters)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var timestamp = (timestampUtc ?? DateTime.UtcNow).ToUniversalTime();
            var name = $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}-{MakeFileSafe(filePath)}-{Guid.NewGuid():N}.json";
            var path = System.IO.Path.Combine(directory, name);
            var document = new JObject
            {
                ["version"] = 1,
                ["filePath"] = filePath,
                ["timestampUtc"] = timestamp.ToString("o", CultureInfo.InvariantCulture),
                ["content"] = content
            };
            // CreateNew also prevents overwriting an existing snapshot if a
            // generated filename is ever reused. Finish writing before pruning.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(document.ToString(Newtonsoft.Json.Formatting.None));
            }

            Prune();
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            // History is best-effort; a storage failure never breaks the caller.
            return null;
        }
    }

    /// <summary>Lists all snapshots, newest first.</summary>
    public IReadOnlyList<EditorSnapshot> List()
    {
        var snapshots = new List<EditorSnapshot>();
        try
        {
            if (!Directory.Exists(directory))
            {
                return snapshots;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    var snapshot = Read(file);
                    if (snapshot is not null)
                    {
                        snapshots.Add(snapshot);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or JsonException)
                {
                    // Broken snapshot files are skipped.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable history directory yields an empty list.
        }

        return snapshots
            .OrderByDescending(snapshot => snapshot.TimestampUtc)
            .ToList();
    }

    /// <summary>Lists snapshots whose name or content contains the query (case-insensitive), newest first.</summary>
    public IReadOnlyList<EditorSnapshot> Search(string query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var all = List();
        if (query.Trim().Length == 0)
        {
            return all;
        }

        return all
            .Where(snapshot => snapshot.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                snapshot.FilePath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                snapshot.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    /// <summary>Deletes the oldest snapshot files beyond MaxSnapshots.</summary>
    public void Prune()
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var files = Directory.EnumerateFiles(directory, "*.json")
                .Select(file => (File: file, Written: File.GetLastWriteTimeUtc(file)))
                .OrderByDescending(entry => entry.Written)
                .ToList();
            for (var index = MaxSnapshots; index < files.Count; index++)
            {
                try
                {
                    File.Delete(files[index].File);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A locked file is pruned on a later run.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Pruning is best-effort.
        }
    }

    private EditorSnapshot? Read(string file)
    {
        var document = JObject.Parse(File.ReadAllText(file));
        if (document["version"]?.Value<int>() != 1 ||
            document["content"] is not JToken contentToken)
        {
            return null;
        }

        var timestamp = document["timestampUtc"]?.Value<string>() is { } stamp &&
            DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : File.GetLastWriteTimeUtc(file);
        return new EditorSnapshot(
            document["filePath"]?.Value<string>() ?? string.Empty,
            timestamp,
            contentToken.Value<string>() ?? string.Empty);
    }

    private static string MakeFileSafe(string filePath)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(filePath.Length);
        foreach (var character in filePath)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        var text = builder.ToString();
        return text.Length == 0 ? "untitled" : text.Substring(Math.Max(0, text.Length - 40));
    }
}
