namespace TSqlFormatter.Core.Layout;

/// <summary>Selects one of two document nodes according to the current group mode.</summary>
public sealed class IfBreakDoc : Doc
{
    public IfBreakDoc(Doc broken, Doc flat)
    {
        Broken = broken ?? throw new ArgumentNullException(nameof(broken));
        Flat = flat ?? throw new ArgumentNullException(nameof(flat));
    }

    public Doc Broken { get; }

    public Doc Flat { get; }
}
