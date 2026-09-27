namespace TSqlFormatter.Core.Formatting;

/// <summary>Line breaks around supported JOIN and ON clauses.</summary>
public sealed class JoinOptions
{
    public JoinOptions(bool clauseNewLine = true, bool conditionNewLine = true)
    {
        ClauseNewLine = clauseNewLine;
        ConditionNewLine = conditionNewLine;
    }

    public bool ClauseNewLine { get; }

    public bool ConditionNewLine { get; }
}
