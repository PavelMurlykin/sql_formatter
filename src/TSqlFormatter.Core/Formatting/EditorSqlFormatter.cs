using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
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
        if ((!original.ParseSucceeded || ContainsCompleteStatements(original, start, end, cancellationToken))
            && isolated.ParseSucceeded && !isolated.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error)) replacement = isolated.Text;
        else
        {
            // An expression/clause may not parse by itself. Format in its original syntax context,
            // then copy only tokens and gaps inside the selection back into the editor buffer.
            // A clause/expression selection does not authorize new tokens at enclosing statement ends.
            var fragmentOptions = scoped.Rules.Catalog.TryGet("syntax.addSemicolons", out _)
                ? scoped.With(rules: scoped.Rules.With("syntax.addSemicolons", RuleValue.FromBoolean(false))) : scoped;
            var formatted = formatter.Format(source, fragmentOptions, new FormatRequest(), cancellationToken);
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
        // A mixed clause/statement selection can contain complete executable statements.
        // Apply syntax only within the copied fragment, after token-to-token layout mapping.
        output = SqlSyntaxPolicy.ApplySafe(output, scoped, parser, SqlDialectVersion.Auto,
            cancellationToken, new SqlTextSpan(start, replacement.Length));
        replacement = output.Substring(start, output.Length - start - (source.Length - end));
        var validated = parser.Parse(output, SqlDialectVersion.Auto, cancellationToken);
        if (original.ParseSucceeded && (!validated.ParseSucceeded || !SqlTokenSafety.PreservesTokens(original, validated, options, cancellationToken, span))) return Rejected(source, span);
        bool changed = output != source;
        return new FormatResult(output, changed, true, changed ? new[] { new TextEdit(new SqlTextSpan(start, end - start), replacement) } : null);
    }
    private static bool ContainsCompleteStatements(SqlParseResult parsed, int start, int end, CancellationToken cancellationToken)
    {
        var tokens = Significant(parsed).Where(t => t.Offset >= start && t.Offset + t.Text.Length <= end
            && t.TokenType is not (TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)).ToArray();
        if (tokens.Length == 0 || parsed.Root is null) return false;
        var statements = SqlSyntaxPolicy.ExecutableStatements(new SqlFragmentWalker().Walk(parsed.Root, cancellationToken))
            .Where(s => s.StartOffset >= start && s.StartOffset + s.FragmentLength <= end).ToArray();
        return statements.Any(s => s.StartOffset == tokens[0].Offset)
            && statements.Any(s => s.StartOffset + s.FragmentLength == tokens[tokens.Length - 1].Offset + tokens[tokens.Length - 1].Text.Length);
    }
    private static IEnumerable<TSqlParserToken> Significant(SqlParseResult parsed) => parsed.Tokens.Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile));
    private static FormatResult Rejected(string source, SqlTextSpan span) => new(source, false, true, diagnostics: new[]
    { new FormatterDiagnostic("TSF3003", "The selected fragment cannot be safely formatted. Select complete SQL tokens or statements.", FormatterDiagnosticSeverity.Warning, span) });
}
