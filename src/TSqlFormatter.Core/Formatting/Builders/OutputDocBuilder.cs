using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats OUTPUT projection and optional INTO target without reordering expressions.</summary>
internal sealed class OutputDocBuilder
{
    private readonly KeywordCase _keywordCase;

    public OutputDocBuilder(FormattingOptions options)
    {
        _keywordCase = options.Keywords.Case;
    }

    public Doc? Build(OutputClause? output, OutputIntoClause? into, SqlDocBuilderContext context)
    {
        if ((output is null) == (into is null)) return null;
        var clause = (TSqlFragment?)output ?? into!;
        var columns = output?.SelectColumns ?? into!.SelectColumns;
        if (columns.Count == 0) return null;

        var source = context.ParseResult.Source;
        var prefix = source.Substring(clause.StartOffset,
            columns[0].StartOffset - clause.StartOffset);
        if (!Regex.IsMatch(prefix, @"^OUTPUT\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;

        var projections = FormatList(columns, context);
        if (projections is null) return null;
        var last = columns[columns.Count - 1];
        var cursor = last.StartOffset + last.FragmentLength;
        var text = CaseKeyword(prefix.Trim()) + " " + projections;
        if (into is not null)
        {
            var target = into.IntoTable;
            if (target is not (NamedTableReference or VariableTableReference)) return null;
            var intoPrefix = source.Substring(cursor, target.StartOffset - cursor);
            if (!Regex.IsMatch(intoPrefix, @"^\s+INTO\s+$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;
            text += " " + CaseKeyword(intoPrefix.Trim()) + " "
                + context.GetOriginalText(target).Trim();
            cursor = target.StartOffset + target.FragmentLength;

            if (into.IntoTableColumns.Count > 0)
            {
                var targetColumns = into.IntoTableColumns;
                var opening = source.Substring(cursor, targetColumns[0].StartOffset - cursor);
                if (!Regex.IsMatch(opening, @"^\s*\(\s*$", RegexOptions.CultureInvariant)) return null;
                var targetNames = FormatList(targetColumns, context);
                if (targetNames is null) return null;
                text += " (" + targetNames + ")";
                var targetLast = targetColumns[targetColumns.Count - 1];
                cursor = targetLast.StartOffset + targetLast.FragmentLength;
                var closing = source.Substring(cursor,
                    clause.StartOffset + clause.FragmentLength - cursor);
                if (!Regex.IsMatch(closing, @"^\s*\)$", RegexOptions.CultureInvariant)) return null;
            }
        }

        var tail = source.Substring(cursor,
            clause.StartOffset + clause.FragmentLength - cursor);
        if (into is null && !string.IsNullOrWhiteSpace(tail)) return null;
        if (into is not null && into.IntoTableColumns.Count == 0
            && !string.IsNullOrWhiteSpace(tail)) return null;
        return new TextDoc(text);
    }

    private string CaseKeyword(string keyword)
    {
        return _keywordCase switch
        {
            KeywordCase.Upper => keyword.ToUpperInvariant(),
            KeywordCase.Lower => keyword.ToLowerInvariant(),
            _ => keyword
        };
    }

    private static string? FormatList<T>(IList<T> elements, SqlDocBuilderContext context)
        where T : TSqlFragment
    {
        var source = context.ParseResult.Source;
        var items = new List<string>();
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            if (index > 0)
            {
                var previous = elements[index - 1];
                var gap = source.Substring(previous.StartOffset + previous.FragmentLength,
                    element.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!Regex.IsMatch(gap, @"^\s*,\s*$", RegexOptions.CultureInvariant)) return null;
            }

            items.Add(context.GetOriginalText(element).Trim());
        }

        return string.Join(", ", items);
    }
}
