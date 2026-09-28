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

        var aligned = !hasInline ? TryAlignAliases(elements, context) : null;
        var breakEvery = hasInline || aligned is not null
            || _options.Select.ColumnLayout == SelectColumnLayout.OnePerLine;
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

            var column = aligned is not null
                ? new TextDoc(aligned[index]) : BuildElement(elements[index], context);
            if (column is null) return null;
            parts.Add(column);
        }

        var body = new IndentDoc(1, new ConcatDoc(parts));
        return breakEvery ? body : new GroupDoc(body);
    }

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
