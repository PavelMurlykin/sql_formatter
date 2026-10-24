using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Analysis;

/// <summary>A positioned static-analysis diagnostic with the rule code and effective severity.</summary>
public sealed class AnalysisDiagnostic
{
    public AnalysisDiagnostic(
        string code,
        AnalysisSeverity severity,
        string message,
        SqlTextSpan span,
        int line,
        int column)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("The diagnostic code must not be empty.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("The diagnostic message must not be empty.", nameof(message));
        }

        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        Code = code;
        Severity = severity;
        Message = message;
        Span = span;
        Line = line;
        Column = column;
    }

    public string Code { get; }

    public AnalysisSeverity Severity { get; }

    public string Message { get; }

    public SqlTextSpan Span { get; }

    /// <summary>One-based line of <see cref="Span"/> start in the analyzed source.</summary>
    public int Line { get; }

    /// <summary>One-based column of <see cref="Span"/> start in the analyzed source.</summary>
    public int Column { get; }
}
