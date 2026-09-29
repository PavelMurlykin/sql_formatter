namespace TSqlFormatter.Core.Layout;

public enum LayoutBreak { Never, Auto, Always }
public enum LayoutList { Auto, OnePerLine }
public enum LayoutComma { Trailing, Leading }

/// <summary>Nullable fields are inherited from a less-specific scope.</summary>
public sealed class SqlLayoutPolicy
{
    public SqlLayoutPolicy(LayoutBreak? before = null, LayoutBreak? after = null,
        LayoutList? list = null, LayoutComma? comma = null,
        int? indentLevels = null, bool? align = null)
    {
        if (indentLevels < 0) throw new ArgumentOutOfRangeException(nameof(indentLevels));
        Before = before;
        After = after;
        List = list;
        Comma = comma;
        IndentLevels = indentLevels;
        Align = align;
    }

    public LayoutBreak? Before { get; }
    public LayoutBreak? After { get; }
    public LayoutList? List { get; }
    public LayoutComma? Comma { get; }
    public int? IndentLevels { get; }
    public bool? Align { get; }

    /// <summary>Merge per field: global, statement, clause, then local overrides.</summary>
    public static SqlLayoutPolicy Resolve(SqlLayoutPolicy global,
        SqlLayoutPolicy? statement = null, SqlLayoutPolicy? clause = null,
        SqlLayoutPolicy? local = null)
    {
        if (global is null) throw new ArgumentNullException(nameof(global));
        var result = global;
        foreach (var layer in new[] { statement, clause, local })
        {
            if (layer is null) continue;
            result = new SqlLayoutPolicy(
                layer.Before ?? result.Before,
                layer.After ?? result.After,
                layer.List ?? result.List,
                layer.Comma ?? result.Comma,
                layer.IndentLevels ?? result.IndentLevels,
                layer.Align ?? result.Align);
        }
        return result;
    }
}
