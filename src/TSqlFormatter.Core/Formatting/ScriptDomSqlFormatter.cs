using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Formats supported T-SQL constructs using ScriptDom tokens and syntax trees.</summary>
public sealed class ScriptDomSqlFormatter : ISqlFormatter
{
    private readonly ISqlParser _parser;

    public ScriptDomSqlFormatter(ISqlParser? parser = null)
    {
        _parser = parser ?? new ScriptDomSqlParser();
    }

    public FormatResult Format(
        string source,
        FormattingOptions options,
        FormatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (request is null) throw new ArgumentNullException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Scope != FormatScope.Document)
        {
            return Unchanged(source, false, new FormatterDiagnostic(
                "TSF3000", "Only document formatting is currently supported.",
                FormatterDiagnosticSeverity.Warning));
        }

        var parsed = _parser.Parse(source, request.Dialect, cancellationToken);
        if (!parsed.ParseSucceeded)
        {
            var diagnostics = parsed.Diagnostics.Select(error => new FormatterDiagnostic(
                "TSF1000", error.Message, FormatterDiagnosticSeverity.Error,
                new SqlTextSpan(error.Offset, 0))).ToArray();
            return new FormatResult(source, false, false, diagnostics: diagnostics);
        }

        var edits = KeywordCasing.GetEdits(parsed, options.Keywords.Case, cancellationToken);
        if (edits.Count == 0)
        {
            return Unchanged(source, true);
        }

        var output = KeywordCasing.Apply(source, edits);
        return new FormatResult(output, true, true, edits);
    }

    private static FormatResult Unchanged(string source, bool parseSucceeded, FormatterDiagnostic? diagnostic = null)
    {
        return new FormatResult(source, false, parseSucceeded,
            diagnostics: diagnostic is null ? null : new[] { diagnostic });
    }
}

internal static class KeywordCasing
{
    public static IReadOnlyList<TextEdit> GetEdits(
        SqlParseResult parsed, KeywordCase keywordCase, CancellationToken cancellationToken)
    {
        if (keywordCase == KeywordCase.Preserve)
        {
            return Array.Empty<TextEdit>();
        }

        var edits = new List<TextEdit>();
        foreach (var token in parsed.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!token.IsKeyword() || token.TokenType == TSqlTokenType.Identifier)
            {
                continue;
            }

            var replacement = keywordCase == KeywordCase.Upper
                ? token.Text.ToUpperInvariant()
                : token.Text.ToLowerInvariant();
            if (!string.Equals(token.Text, replacement, StringComparison.Ordinal))
            {
                edits.Add(new TextEdit(new SqlTextSpan(token.Offset, token.Text.Length), replacement));
            }
        }

        return edits;
    }

    public static string Apply(string source, IReadOnlyList<TextEdit> edits)
    {
        var result = new StringBuilder(source.Length);
        var cursor = 0;
        foreach (var edit in edits)
        {
            result.Append(source, cursor, edit.Span.StartOffset - cursor);
            result.Append(edit.NewText);
            cursor = edit.Span.EndOffset;
        }

        result.Append(source, cursor, source.Length - cursor);
        return result.ToString();
    }
}
