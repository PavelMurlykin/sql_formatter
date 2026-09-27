namespace TSqlFormatter.Configuration;

/// <summary>Pure save eligibility checks, separate from editor event handling.</summary>
public sealed class SqlSaveFormattingPolicy
{
    public bool ShouldFormat(string path, SqlSaveFormattingMode mode)
    {
        if (mode == SqlSaveFormattingMode.Off) return false;
        if (!Enum.IsDefined(typeof(SqlSaveFormattingMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!string.Equals(Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase))
            return false;
        return mode == SqlSaveFormattingMode.CurrentDocument ||
            new SqlFormatterConfigurationDiscovery().FindForFile(path) is not null;
    }
}
