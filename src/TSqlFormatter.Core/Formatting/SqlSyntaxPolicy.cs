using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Opt-in syntax edits derived from parsed identifiers and complete statement boundaries.</summary>
internal static class SqlSyntaxPolicy
{
    public static bool Enabled(FormattingOptions options) =>
        NativeRules.Get(options, "syntax.removeOptionalBrackets").Boolean || NativeRules.Get(options, "syntax.addSemicolons").Boolean;

    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken, SqlTextSpan? allowedSpan = null)
    {
        if (!Enabled(options)) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded) return source;
        var edits = GetEdits(parsed, options, cancellationToken, allowedSpan);
        if (edits.Count == 0) return source;
        string changed = KeywordCasing.Apply(source, edits, cancellationToken);
        return parser.Parse(changed, dialect, cancellationToken).ParseSucceeded ? changed : source;
    }

    public static IReadOnlyList<TextEdit> GetEdits(SqlParseResult parsed, FormattingOptions options,
        CancellationToken cancellationToken, SqlTextSpan? allowedSpan = null)
    {
        if (!Enabled(options) || parsed.Root is null) return Array.Empty<TextEdit>();
        var edits = new Dictionary<int, TextEdit>();
        var protectedSpans = ProtectedSpans(parsed, options);
        var nodes = new SqlFragmentWalker().Walk(parsed.Root, cancellationToken);
        if (NativeRules.Get(options, "syntax.removeOptionalBrackets").Boolean)
        {
            var lexer = SqlParserVersionMap.CreateParser(parsed.ParserVersion, true);
            var valid = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var identifier in nodes.OfType<Identifier>().Where(i => i.QuoteType == QuoteType.SquareBracket))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int start = identifier.StartOffset, length = identifier.FragmentLength;
                var span = new SqlTextSpan(start, length);
                if (!Allowed(span) || identifier.Value is not { Length: > 0 } value) continue;
                if (!valid.TryGetValue(value, out bool removable))
                {
                    using var reader = new System.IO.StringReader(value);
                    var tokens = lexer.GetTokenStream(reader, out var errors).Where(t => t.TokenType != TSqlTokenType.EndOfFile).ToArray();
                    removable = errors.Count == 0 && tokens.Length == 1 && tokens[0].TokenType == TSqlTokenType.Identifier && tokens[0].Text == value;
                    valid[value] = removable;
                }
                if (!removable) continue;
                // Brackets can also separate words and aliases: USE[Db], SELECT[Id]FROM[T], [Id][Alias].
                string replacement = (start > 0 && (WordCharacter(parsed.Source[start - 1]) || parsed.Source[start - 1] == ']') ? " " : "") + value
                    + (span.EndOffset < parsed.Source.Length && WordCharacter(parsed.Source[span.EndOffset]) ? " " : "");
                edits[start] = new TextEdit(span, replacement);
            }
        }
        if (NativeRules.Get(options, "syntax.addSemicolons").Boolean)
        {
            // SelectStatement can also represent a view/inline-function query or INSERT EXEC
            // source. Only executable statements owned by batches, bodies or branches terminate.
            var statements = ExecutableStatements(nodes);
            foreach (var statement in statements.Where(s => s is not LabelStatement))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (statement.FirstTokenIndex < 0 || statement.LastTokenIndex < 0) continue;
                var last = parsed.Tokens.Skip(statement.FirstTokenIndex).Take(statement.LastTokenIndex - statement.FirstTokenIndex + 1)
                    .LastOrDefault(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile
                        or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment));
                if (last is null || last.Text == ";") continue;
                int end = last.Offset + last.Text.Length;
                // Some ScriptDom bodies (notably a function's BEGIN/END) exclude their
                // existing terminator from FragmentLength. Do not append it again.
                var following = parsed.Tokens.Skip(statement.LastTokenIndex + 1).FirstOrDefault(t =>
                    t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile
                        or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment));
                if (following?.Text == ";") continue;
                var span = new SqlTextSpan(end, 0);
                if (Allowed(span) && (allowedSpan is null || statement.StartOffset >= allowedSpan.Value.StartOffset))
                    edits[end] = new TextEdit(span, ";");
            }
        }
        return edits.Values.OrderBy(e => e.Span.StartOffset).ToArray();

        bool Allowed(SqlTextSpan span) => (allowedSpan is null || span.StartOffset >= allowedSpan.Value.StartOffset && span.EndOffset <= allowedSpan.Value.EndOffset)
            && !protectedSpans.Any(p => span.StartOffset >= p.StartOffset && span.StartOffset < p.EndOffset);
    }

    internal static IEnumerable<TSqlStatement> ExecutableStatements(IEnumerable<TSqlFragment> nodes) =>
        nodes.SelectMany(node => node switch
        {
            TSqlBatch batch => batch.Statements.AsEnumerable(),
            StatementList list => list.Statements.AsEnumerable(),
            IfStatement conditional => new[] { conditional.ThenStatement, conditional.ElseStatement }.OfType<TSqlStatement>(),
            WhileStatement loop => new[] { loop.Statement }.OfType<TSqlStatement>(),
            _ => Enumerable.Empty<TSqlStatement>()
        }).Distinct();

    internal static IReadOnlyList<SqlTextSpan> ProtectedSpans(SqlParseResult parsed, FormattingOptions options)
    {
        if (!NativeRules.Get(options, "layout.respectFormattingDirectives").Boolean) return Array.Empty<SqlTextSpan>();
        var spans = new List<SqlTextSpan>();
        int? start = null;
        foreach (var token in parsed.Tokens.Where(t => t.TokenType == TSqlTokenType.SingleLineComment))
        {
            var marker = Regex.Match(token.Text, @"^--\s*SQL Prompt formatting (off|on)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!marker.Success) continue;
            if (marker.Groups[1].Value.Equals("off", StringComparison.OrdinalIgnoreCase)) start ??= token.Offset;
            else if (start is { } offset) { spans.Add(new SqlTextSpan(offset, token.Offset - offset)); start = null; }
        }
        if (start is { } remaining) spans.Add(new SqlTextSpan(remaining, parsed.Source.Length - remaining));
        return spans;
    }
    private static bool WordCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '@' or '#' or '$';
}
