using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Refactoring;

/// <summary>Outcome of a transformation: new document text with the statement replaced, or an error.</summary>
public sealed class SqlTransformationResult
{
    private SqlTransformationResult(
        string? error,
        string newDocumentText,
        string newStatementText,
        SqlTextSpan replacedSpan)
    {
        Error = error;
        NewDocumentText = newDocumentText;
        NewStatementText = newStatementText;
        ReplacedSpan = replacedSpan;
    }

    public string? Error { get; }

    /// <summary>The whole document with the statement replaced and formatting applied.</summary>
    public string NewDocumentText { get; }

    /// <summary>The generated replacement text of the statement alone.</summary>
    public string NewStatementText { get; }

    /// <summary>The replaced statement span in the original source.</summary>
    public SqlTextSpan ReplacedSpan { get; }

    public bool Succeeded => Error is null;

    internal static SqlTransformationResult Failed(string error) =>
        new(error, string.Empty, string.Empty, default);

    internal static SqlTransformationResult Ok(
        string newDocumentText, string newStatementText, SqlTextSpan replacedSpan) =>
        new(null, newDocumentText, newStatementText, replacedSpan);
}
