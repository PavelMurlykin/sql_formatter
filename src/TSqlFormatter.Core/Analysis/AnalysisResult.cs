namespace TSqlFormatter.Core.Analysis;

/// <summary>Outcome of one analysis run.</summary>
public sealed class AnalysisResult
{
    public static readonly AnalysisResult Empty = new AnalysisResult(Array.Empty<AnalysisDiagnostic>());

    public AnalysisResult(IReadOnlyList<AnalysisDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>Diagnostics sorted by source offset, then by rule code.</summary>
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }

    public bool HasDiagnostics => Diagnostics.Count > 0;
}
