using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>The options produced by a valid JSON configuration, or its diagnostics.</summary>
public sealed class ConfigurationParseResult
{
    internal ConfigurationParseResult(FormattingOptions? options, IReadOnlyList<FormatterDiagnostic> diagnostics)
    {
        Options = options;
        Diagnostics = diagnostics;
    }

    public FormattingOptions? Options { get; }

    public IReadOnlyList<FormatterDiagnostic> Diagnostics { get; }

    public bool Succeeded => Options is not null;
}
