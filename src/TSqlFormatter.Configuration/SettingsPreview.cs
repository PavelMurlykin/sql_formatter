using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

public sealed class SettingsPreviewResult
{
    internal SettingsPreviewResult(string source, FormatResult? result, IReadOnlyList<FormatterDiagnostic> diagnostics)
    { Source = source; Result = result; Diagnostics = diagnostics; }
    public string Source { get; }
    public FormatResult? Result { get; }
    public IReadOnlyList<FormatterDiagnostic> Diagnostics { get; }
}

/// <summary>The same Core and configuration precedence as the IDE commands, without editing documents.</summary>
public static class SettingsPreview
{
    public static SettingsPreviewResult Format(string sample, FormattingOptions baseline, string? sqlPath = null,
        CancellationToken cancellationToken = default)
    {
        if (sample.Length > 8192) throw new ArgumentException("Preview is limited to 8192 characters.", nameof(sample));
        string source = "IDE draft (project configuration excluded)";
        var options = baseline;
        if (!string.IsNullOrWhiteSpace(sqlPath))
        {
            var resolved = new SqlFormatterConfigurationResolver().ResolveForSqlFile(sqlPath!, baseline);
            if (!resolved.Succeeded) return new SettingsPreviewResult(sqlPath!, null, resolved.Diagnostics);
            options = resolved.Options!;
            var paths = SqlEditorConfigResolver.FindConfigurationPaths(sqlPath!).ToList();
            var json = new SqlFormatterConfigurationDiscovery().FindForFile(sqlPath!);
            if (json is not null) paths.Add(json);
            source = "IDE draft" + (paths.Count > 0 ? " → " + string.Join(" → ", paths) : " (no project overrides)");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = new ScriptDomSqlFormatter().Format(sample, options, new FormatRequest(), cancellationToken);
        return new SettingsPreviewResult(source, result, result.Diagnostics);
    }
}
