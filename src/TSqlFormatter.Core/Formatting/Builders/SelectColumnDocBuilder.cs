using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds SELECT columns and keeps trailing line comments attached to commas.</summary>
internal sealed class SelectColumnDocBuilder
{
    private readonly SelectOptions _options;

    public SelectColumnDocBuilder(SelectOptions options)
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
            if (!match.Success)
            {
                return null;
            }

            var commentStart = start + match.Groups["comment"].Index;
            var found = comments.FirstOrDefault(item => item.Span.StartOffset == commentStart);
            if (found is null
                || found.Kind != SqlCommentKind.Line
                || found.Placement != SqlTriviaPlacement.Trailing
                || found.AnchorTokenIndex is null
                || context.ParseResult.Tokens[found.AnchorTokenIndex.Value].Text != ","
                || found.Text.TrimEnd('\r', '\n') != match.Groups["comment"].Value)
            {
                return null;
            }

            inline[index - 1] = found;
            hasInline = true;
        }

        var breakEvery = hasInline || _options.ColumnLayout == SelectColumnLayout.OnePerLine;
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

            parts.Add(new TextDoc(context.GetOriginalText(elements[index]).Trim()));
        }

        var body = new IndentDoc(1, new ConcatDoc(parts));
        return breakEvery ? body : new GroupDoc(body);
    }
}
