using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

public sealed class FormatterDiagnostic
{
    public FormatterDiagnostic(
        string code,
        string message,
        FormatterDiagnosticSeverity severity,
        SqlTextSpan? span = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A diagnostic code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A diagnostic message is required.", nameof(message));
        }

        if (!Enum.IsDefined(typeof(FormatterDiagnosticSeverity), severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        Code = code;
        Message = message;
        Severity = severity;
        Span = span;
    }

    public string Code { get; }

    public string Message { get; }

    public FormatterDiagnosticSeverity Severity { get; }

    public SqlTextSpan? Span { get; }
}
