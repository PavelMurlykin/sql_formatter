using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds SELECT columns and keeps trailing comments attached to commas.</summary>
internal sealed class SelectColumnDocBuilder
{
    private readonly FormattingOptions _options;

    public SelectColumnDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(QuerySpecification query, SqlDocBuilderContext context)
    {
        var elements = query.SelectElements;
        if (elements.Count == 0) return null;

        var source = context.ParseResult.Source;
        var comments = new SqlTriviaScanner().Scan(context.ParseResult, context.CancellationToken);
        var inline = new SqlCommentTrivia?[elements.Count - 1];
        var hasInline = false;
        for (var index = 1; index < elements.Count; index++)
        {
            var previous = elements[index - 1];
            var next = elements[index];
            var start = previous.StartOffset + previous.FragmentLength;
            var separator = source.Substring(start, next.StartOffset - start);
            if (Regex.IsMatch(separator, @"^\s*,\s*$", RegexOptions.CultureInvariant))
            {
                continue;
            }

            var match = Regex.Match(separator,
                @"^\s*,[ \t]*(?<comment>--[^\r\n]*)(?:\r\n|\r|\n)\s*$",
                RegexOptions.CultureInvariant);
            var kind = SqlCommentKind.Line;
            if (!match.Success)
            {
                match = Regex.Match(separator,
                    @"^\s*,[ \t]*(?<comment>/\*[\s\S]*?\*/)\s*$",
                    RegexOptions.CultureInvariant);
                kind = SqlCommentKind.Block;
                if (!match.Success) return null;
            }

            var commentStart = start + match.Groups["comment"].Index;
            var found = comments.FirstOrDefault(item => item.Span.StartOffset == commentStart);
            if (found is null
                || found.Kind != kind
                || found.Placement != SqlTriviaPlacement.Trailing
                || found.AnchorTokenIndex is null
                || context.ParseResult.Tokens[found.AnchorTokenIndex.Value].Text != ","
                || (kind == SqlCommentKind.Line
                    ? found.Text.TrimEnd('\r', '\n') : found.Text) != match.Groups["comment"].Value)
            {
                return null;
            }

            inline[index - 1] = found;
            hasInline = true;
        }

        var breakEvery = hasInline || _options.Select.ColumnLayout == SelectColumnLayout.OnePerLine;
        var parts = new List<Doc> { breakEvery ? HardLineDoc.Instance : SoftLineDoc.Instance };
        for (var index = 0; index < elements.Count; index++)
        {
            if (index > 0)
            {
                parts.Add(new TextDoc(","));
                var comment = inline[index - 1];
                if (comment is not null)
                {
                    parts.Add(new TextDoc(" "));
                    parts.Add(new TextDoc(comment.Text.TrimEnd('\r', '\n')));
                    parts.Add(HardLineDoc.Instance);
                }
                else
                {
                    parts.Add(breakEvery ? HardLineDoc.Instance : SoftLineDoc.Instance);
                }
            }

            var column = BuildElement(elements[index], context);
            if (column is null) return null;
            parts.Add(column);
        }

        var body = new IndentDoc(1, new ConcatDoc(parts));
        return breakEvery ? body : new GroupDoc(body);
    }

    private Doc? BuildElement(SelectElement element, SqlDocBuilderContext context)
    {
        if (element is not SelectScalarExpression scalar
            || scalar.Expression is not ScalarSubquery subquery)
        {
            return new TextDoc(context.GetOriginalText(element).Trim());
        }

        var source = context.ParseResult.Source;
        var before = source.Substring(element.StartOffset, subquery.StartOffset - element.StartOffset);
        var after = source.Substring(subquery.StartOffset + subquery.FragmentLength,
            element.StartOffset + element.FragmentLength - subquery.StartOffset - subquery.FragmentLength);
        if (!string.IsNullOrWhiteSpace(before)) return null;
        if (scalar.ColumnName is null)
        {
            if (!string.IsNullOrWhiteSpace(after)) return null;
        }
        else
        {
            var alias = scalar.ColumnName;
            var aliasPrefix = source.Substring(subquery.StartOffset + subquery.FragmentLength,
                alias.StartOffset - subquery.StartOffset - subquery.FragmentLength);
            var aliasTail = source.Substring(alias.StartOffset + alias.FragmentLength,
                element.StartOffset + element.FragmentLength - alias.StartOffset - alias.FragmentLength);
            if (!Regex.IsMatch(aliasPrefix, @"^\s+(?:AS\s+)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !string.IsNullOrWhiteSpace(aliasTail))
            {
                return null;
            }
        }

        var doc = new SubqueryDocBuilder(_options).Build(subquery, context);
        return doc is null ? null : new ConcatDoc(new Doc[]
        {
            doc, new TextDoc(after.TrimEnd())
        });
    }
}
