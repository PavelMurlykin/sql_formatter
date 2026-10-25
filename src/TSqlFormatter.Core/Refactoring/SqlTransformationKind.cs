namespace TSqlFormatter.Core.Refactoring;

/// <summary>The available SQL statement transformations.</summary>
public enum SqlTransformationKind
{
    /// <summary>Single-row INSERT ... VALUES with a column list becomes UPDATE ... SET with a WHERE stub.</summary>
    InsertToUpdate = 0,

    /// <summary>Multi-row VALUES becomes a SELECT ... UNION ALL SELECT ... chain.</summary>
    ValuesToUnionAll = 1,

    /// <summary>EXEC gains a DECLARE @returnCode INT and captures the return code.</summary>
    ExecWithReturnCode = 2
}
