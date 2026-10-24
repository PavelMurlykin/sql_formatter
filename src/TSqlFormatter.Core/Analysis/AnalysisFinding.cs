using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Analysis;

/// <summary>A raw finding produced by a rule, before severity and line data are applied.</summary>
public readonly struct AnalysisFinding
{
    public AnalysisFinding(SqlTextSpan span, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("The finding message must not be empty.", nameof(message));
        }

        Span = span;
        Message = message;
    }

    public SqlTextSpan Span { get; }

    public string Message { get; }
}
