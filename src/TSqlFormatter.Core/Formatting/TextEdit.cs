using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

public sealed class TextEdit
{
    public TextEdit(SqlTextSpan span, string newText)
    {
        Span = span;
        NewText = newText ?? throw new ArgumentNullException(nameof(newText));
    }

    public SqlTextSpan Span { get; }

    public string NewText { get; }
}
