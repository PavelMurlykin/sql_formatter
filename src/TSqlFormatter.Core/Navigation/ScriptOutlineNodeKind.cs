namespace TSqlFormatter.Core.Navigation;

/// <summary>The kind of a script outline node.</summary>
public enum ScriptOutlineNodeKind
{
    Script = 0,

    Batch = 1,

    Module = 2,

    Statement = 3,

    Cte = 4,

    Variable = 5
}
