namespace TSqlFormatter.Core.Formatting;

/// <summary>Opt-in alignment for simple, single-line SQL list items.</summary>
public sealed class AlignmentOptions
{
    public AlignmentOptions(bool selectAliases = false, bool setAssignments = false,
        bool declareTypes = false)
    {
        SelectAliases = selectAliases;
        SetAssignments = setAssignments;
        DeclareTypes = declareTypes;
    }

    public bool SelectAliases { get; }
    public bool SetAssignments { get; }
    public bool DeclareTypes { get; }
}
