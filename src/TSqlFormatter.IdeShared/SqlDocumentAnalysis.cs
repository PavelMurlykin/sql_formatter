using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Analysis;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.IdeShared;

/// <summary>Analysis of one captured SQL snapshot: findings with a line map, or an error.</summary>
public sealed class SqlDocumentAnalysis
{
    public SqlDocumentAnalysis(
        IReadOnlyList<AnalysisDiagnostic>? diagnostics,
        SqlLineMap? lineMap,
        FormatterDiagnostic? error)
    {
        Diagnostics = diagnostics ?? Array.Empty<AnalysisDiagnostic>();
        LineMap = lineMap;
        Error = error;
    }

    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }

    public SqlLineMap? LineMap { get; }

    public FormatterDiagnostic? Error { get; }

    /// <summary>Parses and analyzes the source; rules come from the nearest .tsqlformatter.json.</summary>
    public static SqlDocumentAnalysis Run(string filePath, string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A SQL file path is required.", nameof(filePath));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new AnalysisOptions();
            var configPath = new SqlFormatterConfigurationDiscovery().FindForFile(filePath);
            if (configPath is not null)
            {
                var json = File.ReadAllText(configPath);
                var formatting = new SqlFormatterConfigurationSerializer().Parse(json);
                if (!formatting.Succeeded)
                {
                    return new SqlDocumentAnalysis(null, null, formatting.Diagnostics[0]);
                }

                var configured = new AnalysisConfigurationParser().Parse(json);
                if (!configured.Succeeded)
                {
                    return new SqlDocumentAnalysis(null, null, configured.Diagnostics[0]);
                }

                options = configured.Configuration!.ToAnalysisOptions();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var parseResult = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto, cancellationToken);
            if (parseResult.Diagnostics.Count > 0)
            {
                return new SqlDocumentAnalysis(null, null, new FormatterDiagnostic(
                    "TSF1000", parseResult.Diagnostics[0].Message, FormatterDiagnosticSeverity.Error));
            }

            var result = new SqlAnalyzer().Analyze(parseResult, options, cancellationToken);
            return new SqlDocumentAnalysis(result.Diagnostics, parseResult.LineMap, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or ArgumentException)
        {
            return new SqlDocumentAnalysis(null, null, new FormatterDiagnostic(
                "TSF9000", $"Cannot load SQL configuration: {exception.Message}", FormatterDiagnosticSeverity.Error));
        }
    }
}
