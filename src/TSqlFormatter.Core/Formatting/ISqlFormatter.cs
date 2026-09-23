namespace TSqlFormatter.Core.Formatting;

/// <summary>Contract for T-SQL formatting.</summary>
public interface ISqlFormatter
{
    FormatResult Format(
        string source,
        FormattingOptions options,
        FormatRequest request,
        CancellationToken cancellationToken = default);
}
