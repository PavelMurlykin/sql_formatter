using TSqlFormatter.Core.Analysis;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Outcome of parsing the analysis section: settings on success, TSF2000 diagnostics on failure.</summary>
public sealed class AnalysisConfigurationParseResult
{
    internal AnalysisConfigurationParseResult(
        AnalysisConfiguration? configuration,
        IReadOnlyList<FormatterDiagnostic> diagnostics)
    {
        Configuration = configuration;
        Diagnostics = diagnostics;
    }

    public AnalysisConfiguration? Configuration { get; }

    public IReadOnlyList<FormatterDiagnostic> Diagnostics { get; }

    public bool Succeeded => Configuration is not null;
}
