using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Layout;
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

        if (request.Scope == FormatScope.Statement && request.Selection is null)
        {
            return Unchanged(source, false, new FormatterDiagnostic(
                "TSF3000", "Statement formatting requires a caret position.",
                FormatterDiagnosticSeverity.Warning));
        }

        if (request.ParseFailureBehavior == ParseFailureBehavior.TokenFallback)
        {
            return Unchanged(source, false, new FormatterDiagnostic(
                "TSF3002", "Token fallback formatting is not currently supported.",
                FormatterDiagnosticSeverity.Error));
        }

        var parsed = _parser.Parse(source, request.Dialect, cancellationToken);
        if (!parsed.ParseSucceeded)
        {
            var diagnostics = parsed.Diagnostics.Count == 0
                ? new[] { new FormatterDiagnostic("TSF1000", "Parser did not produce a syntax tree.",
                    FormatterDiagnosticSeverity.Error) }
                : parsed.Diagnostics.Select(error => new FormatterDiagnostic(
                    "TSF1000", error.Message, FormatterDiagnosticSeverity.Error,
                    new SqlTextSpan(error.Offset, 0))).ToArray();
            return request.ParseFailureBehavior == ParseFailureBehavior.Safe
                ? FormatSafeFragments(source, options, request, parsed, diagnostics, cancellationToken)
                : new FormatResult(source, false, false, diagnostics: diagnostics);
        }

        if (request.Scope == FormatScope.Selection)
        {
            return FormatSelection(source, options, request, parsed, cancellationToken);
        }

        if (request.Scope == FormatScope.Statement)
        {
            return FormatStatement(source, options, request, parsed, cancellationToken);
        }

        var builder = new BasicSelectDocBuilder(options);
        var insertBuilder = new InsertDocBuilder(options);
        var updateBuilder = new UpdateDocBuilder(options);
        var deleteBuilder = new DeleteDocBuilder(options);
        var mergeBuilder = new MergeDocBuilder(options);
        var storedCodeBuilder = new StoredCodeDocBuilder(options);
        var document = new SqlDocBuilder(new ISqlFragmentDocBuilder[]
            { builder, insertBuilder, updateBuilder, deleteBuilder, mergeBuilder, storedCodeBuilder })
            .BuildDocument(parsed, cancellationToken);
        if (builder.Applied || insertBuilder.Applied || updateBuilder.Applied
            || deleteBuilder.Applied || mergeBuilder.Applied || storedCodeBuilder.Applied)
        {
            var rendered = new DocRenderer().Render(document, new DocRenderOptions(
                options.General.MaxLineWidth, options.Indent.Size, options.General.LineEnding,
                options.General.FinalNewline, options.Indent.UseTabs), cancellationToken);
            var reparsed = _parser.Parse(rendered, request.Dialect, cancellationToken);
            if (!reparsed.ParseSucceeded)
            {
                return Unchanged(source, true, new FormatterDiagnostic(
                    "TSF3001", "Formatted SQL failed validation and was left unchanged.",
                    FormatterDiagnosticSeverity.Error));
            }

            rendered = KeywordCasing.Apply(rendered,
                KeywordCasing.GetEdits(reparsed, options.Keywords.Case, cancellationToken),
                cancellationToken);
            if (string.Equals(rendered, source, StringComparison.Ordinal))
            {
                return Unchanged(source, true);
            }

            return new FormatResult(rendered, true, true,
                new[] { new TextEdit(new SqlTextSpan(0, source.Length), rendered) });
        }

        var edits = KeywordCasing.GetEdits(parsed, options.Keywords.Case, cancellationToken);
        if (edits.Count == 0)
        {
            return Unchanged(source, true);
        }

        var output = KeywordCasing.Apply(source, edits, cancellationToken);
        return new FormatResult(output, true, true, edits);
    }

    private FormatResult FormatSelection(string source, FormattingOptions options,
        FormatRequest request, SqlParseResult parsed, CancellationToken cancellationToken)
    {
        var selection = request.Selection!.Value;
        if (selection.Length == 0 || selection.EndOffset > source.Length ||
            BoundaryCutsToken(parsed, selection.StartOffset) || BoundaryCutsToken(parsed, selection.EndOffset))
        {
            return Unchanged(source, true, new FormatterDiagnostic(
                "TSF3003", "Selection is empty, outside the document, or cuts through a token.",
                FormatterDiagnosticSeverity.Warning, selection));
        }

        int start = selection.StartOffset;
        int end = selection.EndOffset;
        while (start < end && char.IsWhiteSpace(source[start])) start++;
        while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
        if (start == end || parsed.Root is not TSqlScript script)
        {
            return Unchanged(source, true, new FormatterDiagnostic(
                "TSF3003", "Selection does not contain a formatable statement.",
                FormatterDiagnosticSeverity.Warning, selection));
        }

        var statement = script.Batches.SelectMany(batch => batch.Statements)
            .Where(candidate => candidate.StartOffset <= start &&
                candidate.StartOffset + candidate.FragmentLength >= end)
            .OrderBy(candidate => candidate.FragmentLength)
            .FirstOrDefault();
        if (statement is null || statement.FragmentLength <= 0 ||
            statement.StartOffset < 0 || statement.FragmentLength > source.Length - statement.StartOffset)
        {
            return Unchanged(source, true, new FormatterDiagnostic(
                "TSF3003", "Selection must be within one complete SQL statement.",
                FormatterDiagnosticSeverity.Warning, selection));
        }

        return FormatStatementFragment(source, options, request, statement, selection, cancellationToken);
    }

    private FormatResult FormatStatement(string source, FormattingOptions options,
        FormatRequest request, SqlParseResult parsed, CancellationToken cancellationToken)
    {
        var caret = request.Selection!.Value;
        if (caret.StartOffset > source.Length || parsed.Root is not TSqlScript script)
        {
            return Unchanged(source, true, new FormatterDiagnostic(
                "TSF3004", "Caret is outside a formatable SQL script.",
                FormatterDiagnosticSeverity.Warning, caret));
        }

        var statement = script.Batches.SelectMany(batch => batch.Statements)
            .Where(candidate => candidate.FragmentLength > 0 && candidate.StartOffset >= 0 &&
                candidate.FragmentLength <= source.Length - candidate.StartOffset)
            .OrderBy(candidate => DistanceToStatement(caret.StartOffset, candidate))
            .ThenBy(candidate => candidate.StartOffset)
            .FirstOrDefault();
        if (statement is null)
        {
            return Unchanged(source, true, new FormatterDiagnostic(
                "TSF3004", "No SQL statement was found near the caret.",
                FormatterDiagnosticSeverity.Warning, caret));
        }

        return FormatStatementFragment(source, options, request, statement, caret, cancellationToken);
    }

    private static int DistanceToStatement(int position, TSqlStatement statement)
    {
        int start = statement.StartOffset;
        int end = start + statement.FragmentLength;
        if (position < start) return start - position;
        if (position > end) return position - end;
        return 0;
    }

    private FormatResult FormatStatementFragment(string source, FormattingOptions options,
        FormatRequest request, TSqlStatement statement, SqlTextSpan requestSpan,
        CancellationToken cancellationToken)
    {
        int statementStart = statement.StartOffset;
        int statementLength = statement.FragmentLength;
        string fragment = source.Substring(statementStart, statementLength);
        var scopedOptions = options.With(general: new GeneralOptions(
            options.General.MaxLineWidth, options.General.LineEnding, finalNewline: false));
        var formatted = Format(fragment, scopedOptions,
            new FormatRequest(dialect: request.Dialect), cancellationToken);
        if (!formatted.ParseSucceeded || formatted.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error))
        {
            return Unchanged(source, true, new FormatterDiagnostic(
                request.Scope == FormatScope.Statement ? "TSF3004" : "TSF3003",
                "SQL statement cannot be formatted independently.",
                FormatterDiagnosticSeverity.Warning, requestSpan));
        }

        if (!formatted.Changed) return Unchanged(source, true);
        string output = source.Substring(0, statementStart) + formatted.Text +
            source.Substring(statementStart + statementLength);
        if (!_parser.Parse(output, request.Dialect, cancellationToken).ParseSucceeded)
        {
            return Unchanged(source, true, new FormatterDiagnostic(
                "TSF3001", "Formatted SQL failed validation and was left unchanged.",
                FormatterDiagnosticSeverity.Error));
        }

        return new FormatResult(output, true, true,
            new[] { new TextEdit(new SqlTextSpan(statementStart, statementLength), formatted.Text) });
    }

    private static bool BoundaryCutsToken(SqlParseResult parsed, int offset)
    {
        return parsed.Tokens.Any(token => token.TokenType != TSqlTokenType.WhiteSpace &&
            token.Offset < offset && offset < token.Offset + token.Text.Length);
    }

    private FormatResult FormatSafeFragments(string source, FormattingOptions options,
        FormatRequest request, SqlParseResult parsed, IReadOnlyList<FormatterDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (parsed.Root is not TSqlScript script)
            return new FormatResult(source, false, false, diagnostics: diagnostics);

        var edits = new List<TextEdit>();
        var safeOptions = options.With(general: new GeneralOptions(
            options.General.MaxLineWidth, options.General.LineEnding, finalNewline: false));
        var nextAvailableOffset = 0;
        foreach (var statement in script.Batches.SelectMany(batch => batch.Statements)
                     .OrderBy(statement => statement.StartOffset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = statement.StartOffset;
            var length = statement.FragmentLength;
            if (length <= 0 || start < nextAvailableOffset || start < 0 ||
                length > source.Length - start)
                continue;

            var end = start + length;
            nextAvailableOffset = end;
            if (parsed.Diagnostics.Any(error => error.Offset >= start && error.Offset <= end))
                continue;

            var fragment = source.Substring(start, length);
            var isolated = _parser.Parse(fragment, request.Dialect, cancellationToken);
            if (!isolated.ParseSucceeded) continue;

            var formatted = Format(fragment, safeOptions,
                new FormatRequest(dialect: request.Dialect,
                    parseFailureBehavior: ParseFailureBehavior.Strict), cancellationToken);
            if (!formatted.ParseSucceeded || formatted.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == FormatterDiagnosticSeverity.Error) || !formatted.Changed)
                continue;

            edits.Add(new TextEdit(new SqlTextSpan(start, length), formatted.Text));
        }

        if (edits.Count == 0)
            return new FormatResult(source, false, false, diagnostics: diagnostics);

        var output = new StringBuilder(source.Length);
        var position = 0;
        foreach (var edit in edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Append(source, position, edit.Span.StartOffset - position);
            output.Append(edit.NewText);
            position = edit.Span.EndOffset;
        }
        output.Append(source, position, source.Length - position);
        return new FormatResult(output.ToString(), true, false, edits, diagnostics);
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

    public static string Apply(string source, IReadOnlyList<TextEdit> edits,
        CancellationToken cancellationToken = default)
    {
        var result = new StringBuilder(source.Length);
        var cursor = 0;
        foreach (var edit in edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Append(source, cursor, edit.Span.StartOffset - cursor);
            result.Append(edit.NewText);
            cursor = edit.Span.EndOffset;
        }

        cancellationToken.ThrowIfCancellationRequested();
        result.Append(source, cursor, source.Length - cursor);
        return result.ToString();
    }
}
