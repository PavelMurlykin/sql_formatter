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
                KeywordCasing.GetEdits(reparsed, options, cancellationToken),
                cancellationToken);
            rendered = SqlSpacing.ApplySafe(rendered, options, _parser, request.Dialect, cancellationToken);
            if (string.Equals(rendered, source, StringComparison.Ordinal))
            {
                return Unchanged(source, true);
            }

            return new FormatResult(rendered, true, true,
                new[] { new TextEdit(new SqlTextSpan(0, source.Length), rendered) });
        }

        var edits = KeywordCasing.GetEdits(parsed, options, cancellationToken);
        var cased = KeywordCasing.Apply(source, edits, cancellationToken);
        var output = SqlSpacing.ApplySafe(cased, options, _parser, request.Dialect, cancellationToken);
        if (string.Equals(output, source, StringComparison.Ordinal))
        {
            return Unchanged(source, true);
        }
        return new FormatResult(output, true, true,
            new[] { new TextEdit(new SqlTextSpan(0, source.Length), output) });
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
    private enum TokenCategory { Builtin, DataType, Alias }

    private static readonly HashSet<string> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS", "AVG", "CAST", "CEILING", "COALESCE", "CONCAT", "CONVERT", "COUNT",
        "CURRENT_TIMESTAMP", "DATEADD", "DATEDIFF", "DATENAME", "DATEPART", "DAY",
        "DENSE_RANK", "FLOOR", "GETDATE", "GETUTCDATE", "IIF", "ISNULL", "JSON_VALUE",
        "LAG", "LEAD", "LEFT", "LEN", "LOWER", "LTRIM", "MAX", "MIN", "MONTH",
        "NEWID", "NULLIF", "OBJECT_ID", "POWER", "RANK", "REPLACE", "RIGHT",
        "ROUND", "ROW_NUMBER", "RTRIM", "STRING_AGG", "SUBSTRING", "SUM", "TRIM",
        "TRY_CAST", "TRY_CONVERT", "UPPER", "YEAR"
    };

    public static IReadOnlyList<TextEdit> GetEdits(
        SqlParseResult parsed, FormattingOptions options, CancellationToken cancellationToken)
    {
        var keyword = Read(options, "textCase.keyword").Choice;
        if (keyword == "inherit") keyword = options.Keywords.Case switch
        {
            KeywordCase.Upper => "upper", KeywordCase.Lower => "lower", _ => "preserve"
        };
        var builtin = Read(options, "textCase.builtin").Choice;
        var dataType = Read(options, "textCase.dataType").Choice;
        var identifier = Read(options, "textCase.identifier").Choice;
        var variable = Read(options, "textCase.variable").Choice;
        var alias = Read(options, "textCase.alias").Choice;
        var formatQuoted = Read(options, "textCase.formatQuotedIdentifier").Boolean;
        if (keyword == "preserve" && (builtin is "preserve" or "inherit")
            && (dataType is "preserve" or "inherit")
            && identifier == "preserve" && variable == "preserve" && alias == "preserve")
            return Array.Empty<TextEdit>();

        var categories = builtin != "inherit" || dataType != "inherit" || alias != "preserve"
            ? Classify(parsed, cancellationToken) : new Dictionary<int, TokenCategory>();

        var edits = new List<TextEdit>();
        foreach (var token in parsed.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isQuoted = token.TokenType == TSqlTokenType.QuotedIdentifier;
            var style = categories.TryGetValue(token.Offset, out var category) ? category switch
            {
                TokenCategory.Builtin => builtin,
                TokenCategory.DataType => dataType,
                TokenCategory.Alias => alias,
                _ => "preserve"
            } : token.TokenType == TSqlTokenType.Variable ? variable
                : token.TokenType == TSqlTokenType.Identifier || isQuoted ? identifier
                : token.IsKeyword() ? keyword : "preserve";
            if (style == "inherit") style = token.IsKeyword() && token.TokenType != TSqlTokenType.Identifier
                ? keyword : "preserve";
            if (style == "preserve" || (isQuoted && !formatQuoted)) continue;
            var replacement = isQuoted ? CaseQuoted(token.Text, style) : Case(token.Text, style);
            if (!string.Equals(token.Text, replacement, StringComparison.Ordinal))
            {
                edits.Add(new TextEdit(new SqlTextSpan(token.Offset, token.Text.Length), replacement));
            }
        }

        return edits;
    }

    private static RuleValue Read(FormattingOptions options, string key) =>
        options.Rules.Catalog.TryGet(key, out _) ? options.Rules.Get(key)
            : RuleCatalog.Default.Definitions[key].DefaultValue;

    private static Dictionary<int, TokenCategory> Classify(SqlParseResult parsed,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, TokenCategory>();
        if (parsed.Root is null) return result;
        foreach (var fragment in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (fragment)
            {
                case FunctionCall call when call.CallTarget is null && call.FunctionName is not null
                    && Builtins.Contains(call.FunctionName.Value):
                    result[call.FunctionName.StartOffset] = TokenCategory.Builtin;
                    break;
                case DataTypeReference dataType:
                    var token = parsed.Tokens.FirstOrDefault(candidate => candidate.Offset >= dataType.StartOffset
                        && candidate.Offset < dataType.StartOffset + dataType.FragmentLength
                        && candidate.TokenType is not TSqlTokenType.WhiteSpace);
                    if (token is not null) result[token.Offset] = TokenCategory.DataType;
                    break;
                case SelectScalarExpression scalar when scalar.ColumnName?.Identifier is { } columnAlias:
                    result[columnAlias.StartOffset] = TokenCategory.Alias;
                    break;
                case TableReferenceWithAlias table when table.Alias is not null:
                    result[table.Alias.StartOffset] = TokenCategory.Alias;
                    break;
            }
        }
        return result;
    }

    private static string Case(string text, string style) => style == "upper"
        ? text.ToUpperInvariant() : text.ToLowerInvariant();

    private static string CaseQuoted(string text, string style)
    {
        if (text.Length < 2) return text;
        var open = text[0];
        var close = text[text.Length - 1];
        if (!((open == '[' && close == ']') || (open == '"' && close == '"'))) return text;
        return open + Case(text.Substring(1, text.Length - 2), style) + close;
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
