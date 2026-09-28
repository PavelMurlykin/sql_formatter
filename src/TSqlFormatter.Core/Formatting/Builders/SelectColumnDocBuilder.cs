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
        var separators = new SqlCommentTrivia[elements.Count - 1][];
        var hasSeparatorComments = false;
        for (var index = 1; index < elements.Count; index++)
        {
            var previous = elements[index - 1];
            var next = elements[index];
            var start = previous.StartOffset + previous.FragmentLength;
            var separator = source.Substring(start, next.StartOffset - start);
            var separatorComments = comments.Where(item => item.Span.StartOffset >= start
                    && item.Span.EndOffset <= next.StartOffset).ToArray();
            if (separatorComments.Length == 0)
            {
                if (!Regex.IsMatch(separator, @"^\s*,\s*$", RegexOptions.CultureInvariant))
                    return null;
                separators[index - 1] = Array.Empty<SqlCommentTrivia>();
                continue;
            }

            var firstComment = separatorComments[0];
            var commaPrefix = source.Substring(start, firstComment.Span.StartOffset - start);
            if (!Regex.IsMatch(commaPrefix, @"^\s*,\s*$", RegexOptions.CultureInvariant))
                return null;
            var commaOffset = start + commaPrefix.IndexOf(',');
            if (firstComment.Placement == SqlTriviaPlacement.Leading &&
                CountLineBreaks(commaPrefix.Substring(commaPrefix.IndexOf(',') + 1)) != 1)
                return null;
            var cursor = firstComment.Span.StartOffset;
            for (var commentIndex = 0; commentIndex < separatorComments.Length; commentIndex++)
            {
                var comment = separatorComments[commentIndex];
                var gap = source.Substring(cursor, comment.Span.StartOffset - cursor);
                if (!string.IsNullOrWhiteSpace(gap)
                    || (commentIndex > 0 && CountLineBreaks(gap) > 1)) return null;
                var anchor = comment.AnchorTokenIndex is null ? null :
                    context.ParseResult.Tokens[comment.AnchorTokenIndex.Value];
                var trailingComma = commentIndex == 0
                    && comment.Placement == SqlTriviaPlacement.Trailing
                    && anchor?.Text == "," && anchor.Offset == commaOffset;
                var leadingColumn = comment.Placement == SqlTriviaPlacement.Leading
                    && anchor?.Offset == next.StartOffset;
                if (!trailingComma && !leadingColumn) return null;
                cursor = comment.Span.EndOffset;
            }

            var tail = source.Substring(cursor, next.StartOffset - cursor);
            if (!string.IsNullOrWhiteSpace(tail) || CountLineBreaks(tail) > 1)
                return null;

            separators[index - 1] = separatorComments;
            hasSeparatorComments = true;
        }

        var aligned = !hasSeparatorComments ? TryAlignAliases(elements, context) : null;
        var breakEvery = hasSeparatorComments || aligned is not null
            || _options.Select.ColumnLayout == SelectColumnLayout.OnePerLine;
        var parts = new List<Doc> { breakEvery ? HardLineDoc.Instance : SoftLineDoc.Instance };
        for (var index = 0; index < elements.Count; index++)
        {
            if (index > 0)
            {
                parts.Add(new TextDoc(","));
                var separatorComments = separators[index - 1];
                if (separatorComments.Length > 0)
                {
                    parts.Add(separatorComments[0].Placement == SqlTriviaPlacement.Trailing
                        ? new TextDoc(" ") : HardLineDoc.Instance);
                    foreach (var comment in separatorComments)
                    {
                        parts.Add(new TextDoc(comment.Text.TrimEnd('\r', '\n')));
                        parts.Add(HardLineDoc.Instance);
                    }
                }
                else
                {
                    parts.Add(breakEvery ? HardLineDoc.Instance : SoftLineDoc.Instance);
                }
            }

            var column = aligned is not null
                ? new TextDoc(aligned[index]) : BuildElement(elements[index], context);
            if (column is null) return null;
            parts.Add(column);
        }

        var body = new IndentDoc(1, new ConcatDoc(parts));
        return breakEvery ? body : new GroupDoc(body);
    }

    private static int CountLineBreaks(string text) =>
        Regex.Matches(text, @"\r\n|\r|\n", RegexOptions.CultureInvariant).Count;

    private string[]? TryAlignAliases(IList<SelectElement> elements, SqlDocBuilderContext context)
    {
        if (!_options.Alignment.SelectAliases || _options.Indent.UseTabs || elements.Count < 2)
            return null;
        var expressions = new string[elements.Count];
        var aliases = new string[elements.Count];
        for (int index = 0; index < elements.Count; index++)
        {
            if (elements[index] is not SelectScalarExpression { Expression: { } expression,
                    ColumnName: { } alias } scalar) return null;
            var source = context.ParseResult.Source;
            var before = source.Substring(scalar.StartOffset, expression.StartOffset - scalar.StartOffset);
            var gap = source.Substring(expression.StartOffset + expression.FragmentLength,
                alias.StartOffset - expression.StartOffset - expression.FragmentLength);
            var after = source.Substring(alias.StartOffset + alias.FragmentLength,
                scalar.StartOffset + scalar.FragmentLength - alias.StartOffset - alias.FragmentLength);
            if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(after)
                || !Regex.IsMatch(gap, @"^\s+AS\s+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return null;
            expressions[index] = context.GetOriginalText(expression).Trim();
            aliases[index] = context.GetOriginalText(alias).Trim();
            if (expressions[index].IndexOfAny(new[] { '\r', '\n' }) >= 0
                || aliases[index].IndexOfAny(new[] { '\r', '\n' }) >= 0
                || expressions[index].Contains("/*") || expressions[index].Contains("--")) return null;
        }

        int width = expressions.Max(value => value.Length);
        if (Enumerable.Range(0, expressions.Length).Any(index => _options.Indent.Size + width + 4
                + aliases[index].Length > _options.General.MaxLineWidth)) return null;
        return expressions.Select((value, index) =>
            value.PadRight(width) + " AS " + aliases[index]).ToArray();
    }

    private Doc? BuildElement(SelectElement element, SqlDocBuilderContext context)
    {
        if (element is not SelectScalarExpression scalar
            || scalar.Expression is not (ScalarSubquery or CaseExpression or FunctionCall))
        {
            return new TextDoc(context.GetOriginalText(element).Trim());
        }

        var expression = scalar.Expression;
        if (expression is FunctionCall ordinary && ordinary.OverClause is null)
        {
            return new TextDoc(context.GetOriginalText(element).Trim());
        }
        var source = context.ParseResult.Source;
        var before = source.Substring(element.StartOffset, expression.StartOffset - element.StartOffset);
        var after = source.Substring(expression.StartOffset + expression.FragmentLength,
            element.StartOffset + element.FragmentLength - expression.StartOffset - expression.FragmentLength);
        if (!string.IsNullOrWhiteSpace(before)) return null;
        if (scalar.ColumnName is null)
        {
            if (!string.IsNullOrWhiteSpace(after)) return null;
        }
        else
        {
            var alias = scalar.ColumnName;
            var aliasPrefix = source.Substring(expression.StartOffset + expression.FragmentLength,
                alias.StartOffset - expression.StartOffset - expression.FragmentLength);
            var aliasTail = source.Substring(alias.StartOffset + alias.FragmentLength,
                element.StartOffset + element.FragmentLength - alias.StartOffset - alias.FragmentLength);
            if (!Regex.IsMatch(aliasPrefix, @"^\s+(?:AS\s+)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !string.IsNullOrWhiteSpace(aliasTail))
            {
                return null;
            }
        }

        var doc = expression switch
        {
            ScalarSubquery subquery => new SubqueryDocBuilder(_options).Build(subquery, context),
            CaseExpression caseExpression => new CaseDocBuilder().Build(caseExpression, context),
            FunctionCall function => new WindowFunctionDocBuilder(_options).Build(function, context),
            _ => null
        };
        return doc is null ? null : new ConcatDoc(new Doc[]
        {
            doc, new TextDoc(after.TrimEnd())
        });
    }
}
