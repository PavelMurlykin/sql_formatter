using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats OVER clauses with partition and ordering lists.</summary>
internal sealed class WindowFunctionDocBuilder
{
    private readonly FormattingOptions _options;

    public WindowFunctionDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(FunctionCall function, SqlDocBuilderContext context)
    {
        var over = function.OverClause;
        if (over is null || over.WindowName is not null || over.WindowFrameClause is not null)
        {
            return null;
        }

        var source = context.ParseResult.Source;
        var functionPrefix = source.Substring(function.StartOffset,
            over.StartOffset - function.StartOffset);
        var functionTail = source.Substring(over.StartOffset + over.FragmentLength,
            function.StartOffset + function.FragmentLength - over.StartOffset - over.FragmentLength);
        if (!Regex.IsMatch(functionPrefix, @"\)\s*$", RegexOptions.CultureInvariant)
            || !string.IsNullOrWhiteSpace(functionTail)) return null;

        var partitions = over.Partitions;
        var order = over.OrderByClause;
        var firstStart = partitions.Count > 0 ? partitions[0].StartOffset
            : order?.StartOffset ?? over.StartOffset + over.FragmentLength - 1;
        var header = source.Substring(over.StartOffset, firstStart - over.StartOffset);
        var headerPattern = partitions.Count > 0
            ? @"^OVER\s*\(\s*PARTITION\s+BY\s+$"
            : @"^OVER\s*\(\s*$";
        if (!Regex.IsMatch(header, headerPattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;

        var content = new List<Doc>();
        var cursor = firstStart;
        if (partitions.Count > 0)
        {
            var values = new List<string>();
            for (var index = 0; index < partitions.Count; index++)
            {
                var item = partitions[index];
                if (index > 0)
                {
                    var previous = partitions[index - 1];
                    var gap = source.Substring(previous.StartOffset + previous.FragmentLength,
                        item.StartOffset - previous.StartOffset - previous.FragmentLength);
                    if (!Regex.IsMatch(gap, @"^\s*,\s*$", RegexOptions.CultureInvariant)) return null;
                }

                values.Add(context.GetOriginalText(item).Trim());
            }

            var partitionKeyword = Regex.Match(header, @"PARTITION\s+BY",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Value;
            var normalizedPartitionKeyword = Regex.Replace(partitionKeyword, @"\s+", " ");
            normalizedPartitionKeyword = _options.Keywords.Case switch
            {
                KeywordCase.Upper => normalizedPartitionKeyword.ToUpperInvariant(),
                KeywordCase.Lower => normalizedPartitionKeyword.ToLowerInvariant(),
                _ => normalizedPartitionKeyword
            };
            content.Add(HardLineDoc.Instance);
            content.Add(new TextDoc(normalizedPartitionKeyword + " " + string.Join(", ", values)));
            var last = partitions[partitions.Count - 1];
            cursor = last.StartOffset + last.FragmentLength;
        }

        if (order is not null)
        {
            var gap = source.Substring(cursor, order.StartOffset - cursor);
            var orderDoc = order.All ? null : new ListClauseDocBuilder().Build(order,
                order.OrderByElements, @"ORDER\s+BY", _options.Clauses.OrderByLayout, context);
            if (!string.IsNullOrWhiteSpace(gap) || orderDoc is null) return null;
            content.Add(HardLineDoc.Instance);
            content.Add(orderDoc);
            cursor = order.StartOffset + order.FragmentLength;
        }

        var suffix = source.Substring(cursor,
            over.StartOffset + over.FragmentLength - cursor);
        if (!Regex.IsMatch(suffix, @"^\s*\)$", RegexOptions.CultureInvariant)) return null;

        var overKeyword = Regex.Match(header, @"^OVER",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Value;
        if (content.Count == 0)
        {
            return new TextDoc(functionPrefix.TrimEnd() + " " + overKeyword + " ()");
        }

        return new ConcatDoc(new Doc[]
        {
            new TextDoc(functionPrefix.TrimEnd() + " " + overKeyword + " ("),
            new IndentDoc(1, new ConcatDoc(content)),
            HardLineDoc.Instance,
            new TextDoc(")")
        });
    }
}
