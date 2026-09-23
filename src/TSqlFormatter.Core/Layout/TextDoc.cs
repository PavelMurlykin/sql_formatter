namespace TSqlFormatter.Core.Layout;

/// <summary>Literal text to emit without changing its content.</summary>
public sealed class TextDoc : Doc
{
    public TextDoc(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public string Text { get; }
}
