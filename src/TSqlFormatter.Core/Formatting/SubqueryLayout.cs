using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Independent subquery boundaries and five context-specific one-line policies.</summary>
internal static class SubqueryLayout
{
    private static readonly Regex Newlines = new(@"[ \t]*(?:\r\n|\n|\r)[ \t]*",
        RegexOptions.CultureInvariant);
    private static readonly Regex Words = new(@"[A-Za-z_@#][A-Za-z_0-9@#$]*|[0-9]+",
        RegexOptions.CultureInvariant);

    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (NativeRules.Get(options, "subquery.useSelectFormatting").Boolean) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var wrappers = Collect(parsed.Root, cancellationToken);
        var editor = new SqlTokenGapEditor(parsed, options);
        foreach (var wrapper in wrappers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var open = editor.FindLast(wrapper.Start, wrapper.Query.StartOffset, token => token.Text == "(");
            var queryEnd = wrapper.Query.StartOffset + wrapper.Query.FragmentLength;
            var close = editor.Find(queryEnd, wrapper.End, token => token.Text == ")");
            if (open is null || close is null) continue;
            editor.Before(open.Offset, Break("subquery.breakBeforeOpen"));
            var afterOpen = Break("subquery.breakAfterOpen");
            var indent = NativeRules.Get(options, "subquery.indent").Indent;
            editor.Before(wrapper.Query.StartOffset, afterOpen,
                afterOpen == "never" && !indent.OnNewLineOnly ? indent : null);
            editor.Before(close.Offset, Break("subquery.breakBeforeClose"));
            editor.After(close.Offset, Break("subquery.breakAfterClose"));
        }
        var changed = editor.Apply(cancellationToken);
        var checkedLayout = parser.Parse(changed, dialect, cancellationToken);
        if (!checkedLayout.ParseSucceeded || !SqlSpacing.SameTokens(parsed, checkedLayout)) return source;
        changed = IndentBodies(changed, options, parser, dialect, cancellationToken);
        return Compact(changed, options, parser, dialect, cancellationToken);

        string Break(string key) => NativeRules.Get(options, key).Choice;
    }

    private static string IndentBodies(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        var indent = NativeRules.Get(options, "subquery.indent").Indent;
        if (!indent.Enabled || options.Indent.UseTabs) return source;
        var initial = parser.Parse(source, dialect, cancellationToken);
        if (!initial.ParseSucceeded || initial.Root is null) return source;
        var count = Collect(initial.Root, cancellationToken).Count;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = parser.Parse(source, dialect, cancellationToken);
            if (!parsed.ParseSucceeded || parsed.Root is null) return source;
            var wrappers = Collect(parsed.Root, cancellationToken).OrderBy(wrapper => wrapper.Start).ToArray();
            if (index >= wrappers.Length) return source;
            var wrapper = wrappers[index];
            var editor = new SqlTokenGapEditor(parsed, options);
            var open = editor.FindLast(wrapper.Start, wrapper.Query.StartOffset, token => token.Text == "(");
            if (open is null) continue;
            var firstLine = source.LastIndexOfAny(new[] { '\r', '\n' },
                Math.Max(0, wrapper.Query.StartOffset - 1)) + 1;
            if (source.Substring(firstLine, wrapper.Query.StartOffset - firstLine)
                .Any(ch => ch is not (' ' or '\t'))) continue;
            var openLine = source.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, open.Offset - 1)) + 1;
            var baseIndent = source.Substring(openLine).TakeWhile(ch => ch is ' ' or '\t').Count();
            var currentIndent = wrapper.Query.StartOffset - firstLine;
            var targetIndent = indent.Column(baseIndent, options.Indent.Size);
            var delta = targetIndent - currentIndent;
            if (delta == 0) continue;
            var end = wrapper.Query.StartOffset + wrapper.Query.FragmentLength;
            var edits = new List<TextEdit>();
            foreach (Match line in Regex.Matches(source, @"(?m)^[ \t]*", RegexOptions.CultureInvariant))
            {
                if (line.Index < firstLine || line.Index > end) continue;
                if (parsed.Tokens.Any(token => token.Offset < line.Index
                    && token.Offset + token.Text.Length > line.Index)) continue;
                var newLength = Math.Max(0, line.Length + delta);
                if (newLength == line.Length) continue;
                edits.Add(new TextEdit(new SqlTextSpan(line.Index, line.Length),
                    new string(' ', newLength)));
            }
            if (edits.Count == 0) continue;
            var candidate = KeywordCasing.Apply(source, edits, cancellationToken);
            var validated = parser.Parse(candidate, dialect, cancellationToken);
            if (validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated)) source = candidate;
        }
        return source;
    }

    private static string Compact(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var wrappers = Collect(parsed.Root, cancellationToken).OrderBy(wrapper => wrapper.Start).ToArray();
        var editor = new SqlTokenGapEditor(parsed, options);
        var edits = new List<TextEdit>();
        var previousEnd = -1;
        foreach (var wrapper in wrappers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (wrapper.Start < previousEnd) continue;
            var open = editor.FindLast(wrapper.Start, wrapper.Query.StartOffset, token => token.Text == "(");
            var queryEnd = wrapper.Query.StartOffset + wrapper.Query.FragmentLength;
            var close = editor.Find(queryEnd, wrapper.End, token => token.Text == ")");
            if (open is null || close is null || close.Offset < open.Offset) continue;
            var key = "subquery.singleLine." + wrapper.Category;
            var any = NativeRules.Get(options, key + ".any").Boolean;
            var fits = NativeRules.Get(options, key + ".whenFitsMargin").Boolean;
            var maxWords = NativeRules.Get(options, key + ".maxWords").Threshold;
            var maxCharacters = NativeRules.Get(options, key + ".maxCharacters").Threshold;
            if (!any && !fits && !maxWords.Enabled && !maxCharacters.Enabled) continue;
            var tokens = parsed.Tokens.Where(token => token.Offset >= open.Offset && token.Offset <= close.Offset
                && token.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).ToArray();
            if (tokens.Any(token => token.TokenType is TSqlTokenType.SingleLineComment
                    or TSqlTokenType.MultilineComment || token.Text?.IndexOfAny(new[] { '\r', '\n' }) >= 0))
                continue;
            var inner = source.Substring(open.Offset + 1, close.Offset - open.Offset - 1);
            var flat = Newlines.Replace(inner, " ").Trim();
            var candidate = "(" + flat + ")";
            var current = source.Substring(open.Offset, close.Offset + 1 - open.Offset);
            if (candidate == current) continue;
            var lineStart = source.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, open.Offset - 1)) + 1;
            var lineEnd = source.IndexOfAny(new[] { '\r', '\n' }, close.Offset + 1);
            if (lineEnd < 0) lineEnd = source.Length;
            var fitsMargin = open.Offset - lineStart + candidate.Length + lineEnd - close.Offset - 1
                <= options.General.MaxLineWidth;
            if (!(any || fits && fitsMargin
                    || maxWords.Enabled && Words.Matches(flat).Count < maxWords.Value
                    || maxCharacters.Enabled && flat.Length < maxCharacters.Value)) continue;
            edits.Add(new TextEdit(new SqlTextSpan(open.Offset, current.Length), candidate));
            previousEnd = close.Offset + 1;
        }
        if (edits.Count == 0) return source;
        var changed = KeywordCasing.Apply(source, edits, cancellationToken);
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;
    }

    private static IReadOnlyList<Wrapper> Collect(TSqlFragment root, CancellationToken cancellationToken)
    {
        var fragments = new SqlFragmentWalker().Walk(root, cancellationToken).ToArray();
        var categories = new Dictionary<int, string>();
        foreach (var fragment in fragments)
        {
            switch (fragment)
            {
                case ExistsPredicate { Subquery: { QueryExpression: { } query } }:
                    categories[query.StartOffset] = "allAnySomeExists";
                    break;
                case SubqueryComparisonPredicate { Subquery: { QueryExpression: { } query } }:
                    categories[query.StartOffset] = "allAnySomeExists";
                    break;
                case InPredicate { Subquery: { QueryExpression: { } query } }:
                    categories[query.StartOffset] = "inOperator";
                    break;
                case QueryDerivedTable { QueryExpression: { } query }:
                    categories[query.StartOffset] = "fromList";
                    break;
                case CommonTableExpression { QueryExpression: { } query }:
                    categories[query.StartOffset] = "cteQueries";
                    break;
            }
        }
        var wrappers = new List<Wrapper>();
        foreach (var fragment in fragments)
        {
            switch (fragment)
            {
                case ScalarSubquery { QueryExpression: { } query } scalar:
                    wrappers.Add(new Wrapper(scalar.StartOffset,
                        scalar.StartOffset + scalar.FragmentLength, query,
                        categories.TryGetValue(query.StartOffset, out var category) ? category : "other"));
                    break;
                case QueryDerivedTable { QueryExpression: { } query } derived:
                    wrappers.Add(new Wrapper(derived.StartOffset,
                        derived.StartOffset + derived.FragmentLength, query, "fromList"));
                    break;
                case CommonTableExpression { QueryExpression: { } query } cte:
                    wrappers.Add(new Wrapper(cte.StartOffset,
                        cte.StartOffset + cte.FragmentLength, query, "cteQueries"));
                    break;
            }
        }
        return wrappers;
    }

    private sealed class Wrapper
    {
        public Wrapper(int start, int end, QueryExpression query, string category)
        {
            Start = start;
            End = end;
            Query = query;
            Category = category;
        }

        public int Start { get; }
        public int End { get; }
        public QueryExpression Query { get; }
        public string Category { get; }
    }
}
