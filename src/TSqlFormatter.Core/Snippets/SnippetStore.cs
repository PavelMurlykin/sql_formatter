namespace TSqlFormatter.Core.Snippets;

/// <summary>
/// Loads built-in snippets and user snippets from
/// %USERPROFILE%\.tsqlformatter\snippets\*.json (created on demand).
/// User snippets override built-ins with the same shortcut or name.
/// Invalid user files are skipped.
/// </summary>
public sealed class SnippetStore
{
    private const string BuiltInJson = @"
[
  {
    ""name"": ""Stored procedure"",
    ""shortcut"": ""proc"",
    ""description"": ""CREATE OR ALTER PROCEDURE with TRY/CATCH"",
    ""body"": ""CREATE OR ALTER PROCEDURE dbo.$ProcedureName$\n    @Parameter1 INT\nAS\nBEGIN\n    SET NOCOUNT ON;\n    BEGIN TRY\n        $statements$\n    END TRY\n    BEGIN CATCH\n        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;\n        THROW;\n    END CATCH\nEND\n""
  },
  {
    ""name"": ""TRY/CATCH"",
    ""shortcut"": ""try"",
    ""description"": ""BEGIN TRY ... END CATCH with rethrow"",
    ""body"": ""BEGIN TRY\n    $statements$\nEND TRY\nBEGIN CATCH\n    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;\n    THROW;\nEND CATCH\n""
  },
  {
    ""name"": ""Transaction"",
    ""shortcut"": ""tran"",
    ""description"": ""BEGIN TRANSACTION with rollback and conditional commit"",
    ""body"": ""BEGIN TRANSACTION;\nBEGIN TRY\n    $statements$\nEND TRY\nBEGIN CATCH\n    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;\n    THROW;\nEND CATCH;\nIF @@TRANCOUNT > 0 COMMIT TRANSACTION;\n""
  },
  {
    ""name"": ""SELECT with JOIN"",
    ""shortcut"": ""select"",
    ""description"": ""SELECT template with an INNER JOIN"",
    ""body"": ""SELECT\n    $columns$\nFROM dbo.$Table$ AS t\nINNER JOIN dbo.$JoinedTable$ AS j ON j.Id = t.$JoinColumn$;\n""
  },
  {
    ""name"": ""INSERT"",
    ""shortcut"": ""insert"",
    ""description"": ""INSERT template with a column list"",
    ""body"": ""INSERT INTO dbo.$Table$\n    ($columns$)\nVALUES\n    ($values$);\n""
  }
]
";

    private readonly string? _userDirectory;

    public SnippetStore(string? userDirectory = null)
    {
        _userDirectory = userDirectory;
    }

    public static string DefaultUserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".tsqlformatter", "snippets");

    public static IReadOnlyList<SqlSnippet> BuiltIn { get; } = SnippetJson.Parse(BuiltInJson);

    public IReadOnlyList<SqlSnippet> Load()
    {
        var merged = new Dictionary<string, SqlSnippet>(StringComparer.OrdinalIgnoreCase);
        foreach (var snippet in BuiltIn)
        {
            merged[snippet.Key] = snippet;
        }

        var directory = _userDirectory ?? DefaultUserDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    if (SnippetJson.TryParse(json, out var snippets, out _))
                    {
                        foreach (var snippet in snippets)
                        {
                            merged[snippet.Key] = snippet;
                        }
                    }
                }
                catch (Exception)
                {
                    // Invalid user snippet files are skipped without failing the load.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Built-in snippets stay available when the user directory is unreadable.
        }

        return merged.Values
            .OrderBy(snippet => snippet.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
