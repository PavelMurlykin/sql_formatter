namespace TSqlFormatter.Core.Formatting;

/// <summary>Contract for a future T-SQL formatter implementation.</summary>
public interface ISqlFormatter
{
    FormatResult Format(
        string source,
        FormattingOptions options,
        FormatRequest request,
        CancellationToken cancellationToken = default);
}
