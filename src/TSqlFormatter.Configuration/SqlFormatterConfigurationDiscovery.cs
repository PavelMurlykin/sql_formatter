namespace TSqlFormatter.Configuration;

/// <summary>Finds the nearest config beside or above a SQL file, stopping at a repository root.</summary>
public sealed class SqlFormatterConfigurationDiscovery
{
    public string? FindForFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A SQL file path is required.", nameof(filePath));

        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        while (directory is not null)
        {
            var configPath = Path.Combine(directory.FullName, SqlFormatterConfigurationSerializer.FileName);
            if (File.Exists(configPath) || Directory.Exists(configPath)) return configPath;

            // .git can be a directory or a worktree marker file.
            var marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker)) return null;
            directory = directory.Parent;
        }

        return null;
    }
}
