namespace TSqlFormatter.Core.Formatting;

/// <summary>Line breaks in supported WHERE and HAVING predicates.</summary>
public sealed class WhereOptions
{
    public WhereOptions(bool conditionNewLine = true, bool booleanOperatorNewLine = true)
    {
        ConditionNewLine = conditionNewLine;
        BooleanOperatorNewLine = booleanOperatorNewLine;
    }

    public bool ConditionNewLine { get; }

    public bool BooleanOperatorNewLine { get; }
}
