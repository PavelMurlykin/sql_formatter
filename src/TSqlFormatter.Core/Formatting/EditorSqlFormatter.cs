using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Editor command semantics: a selection is an exact edit boundary, including multi-statement selections.</summary>
public sealed class EditorSqlFormatter
{
    public FormatResult Format(string source, FormattingOptions options, SqlTextSpan? selection = null,
        CancellationToken cancellationToken = default)
    {
        var formatter = new ScriptDomSqlFormatter();
        if (selection is null || selection.Value.Length == 0)
            return formatter.Format(source, options, new FormatRequest(), cancellationToken);
        var span = selection.Value;
        if (span.EndOffset > source.Length) return Rejected(source, span);
        var parser = new ScriptDomSqlParser();
        var original = parser.Parse(source, SqlDialectVersion.Auto, cancellationToken);
        if (original.Tokens.Any(t => t.TokenType != TSqlTokenType.WhiteSpace &&
            (t.Offset < span.StartOffset && span.StartOffset < t.Offset + t.Text.Length ||
             t.Offset < span.EndOffset && span.EndOffset < t.Offset + t.Text.Length))) return Rejected(source, span);
        int start = span.StartOffset, end = span.EndOffset;
        while (start < end && char.IsWhiteSpace(source[start])) start++;
        while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
        if (start == end) return new FormatResult(source, false, original.ParseSucceeded);
        var scoped = options.With(general: new GeneralOptions(options.General.MaxLineWidth, options.General.LineEnding, false));
        var isolated = formatter.Format(source.Substring(start, end - start), scoped, new FormatRequest(), cancellationToken);
        string replacement;
        if (isolated.ParseSucceeded && !isolated.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error)) replacement = isolated.Text;
        else
        {
            // An expression/clause may not parse by itself. Format in its original syntax context,
            // then copy only tokens and gaps inside the selection back into the editor buffer.
            var formatted = formatter.Format(source, scoped, new FormatRequest(), cancellationToken);
            if (!formatted.ParseSucceeded || formatted.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error)) return Rejected(source, span);
            var before = Significant(original).ToArray();
            var after = Significant(parser.Parse(formatted.Text, SqlDialectVersion.Auto, cancellationToken)).ToArray();
            if (before.Length != after.Length) return Rejected(source, span);
            int first = Array.FindIndex(before, t => t.Offset >= start);
            int last = Array.FindLastIndex(before, t => t.Offset + t.Text.Length <= end);
            if (first < 0 || last < first) return Rejected(source, span);
            replacement = formatted.Text.Substring(after[first].Offset, after[last].Offset + after[last].Text.Length - after[first].Offset);
        }
        string output = source.Substring(0, start) + replacement + source.Substring(end);
        var validated = parser.Parse(output, SqlDialectVersion.Auto, cancellationToken);
        if (original.ParseSucceeded && (!validated.ParseSucceeded || !SqlTokenSafety.PreservesTokens(original, validated, options, cancellationToken, span))) return Rejected(source, span);
        bool changed = output != source;
        return new FormatResult(output, changed, true, changed ? new[] { new TextEdit(new SqlTextSpan(start, end - start), replacement) } : null);
    }
    private static IEnumerable<TSqlParserToken> Significant(SqlParseResult parsed) => parsed.Tokens.Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile));
    private static FormatResult Rejected(string source, SqlTextSpan span) => new(source, false, true, diagnostics: new[]
    { new FormatterDiagnostic("TSF3003", "The selected fragment cannot be safely formatted. Select complete SQL tokens or statements.", FormatterDiagnosticSeverity.Warning, span) });
}
