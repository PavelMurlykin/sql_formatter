using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds a clause separator containing only adjacent leading comments.</summary>
internal sealed class LeadingCommentDocBuilder
{
    public Doc? Build(int start, int clauseStart, SqlDocBuilderContext context)
    {
        var source = context.ParseResult.Source;
        var comments = new SqlTriviaScanner().Scan(context.ParseResult, context.CancellationToken)
            .Where(item => item.Span.StartOffset >= start && item.Span.EndOffset <= clauseStart)
            .ToArray();
        var parts = new List<Doc>();
        var cursor = start;
        foreach (var comment in comments)
        {
            if (!string.IsNullOrWhiteSpace(source.Substring(cursor, comment.Span.StartOffset - cursor))
                || comment.Placement != SqlTriviaPlacement.Leading
                || comment.AnchorTokenIndex is null
                || context.ParseResult.Tokens[comment.AnchorTokenIndex.Value].Offset != clauseStart)
            {
                return null;
            }

            parts.Add(HardLineDoc.Instance);
            parts.Add(new TextDoc(comment.Text.TrimEnd('\r', '\n')));
            cursor = comment.Span.EndOffset;
        }

        if (!string.IsNullOrWhiteSpace(source.Substring(cursor, clauseStart - cursor)))
        {
            return null;
        }

        parts.Add(HardLineDoc.Instance);
        return new ConcatDoc(parts);
    }
}
