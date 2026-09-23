using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds a comma-delimited GROUP BY or ORDER BY clause.</summary>
internal sealed class ListClauseDocBuilder
{
    public Doc? Build<T>(
        TSqlFragment clause,
        IList<T> elements,
        string keywordPattern,
        ClauseItemLayout layout,
        SqlDocBuilderContext context)
        where T : TSqlFragment
    {
        if (elements.Count == 0)
        {
            return null;
        }

        var source = context.ParseResult.Source;
        var first = elements[0];
        var prefix = source.Substring(clause.StartOffset, first.StartOffset - clause.StartOffset);
        if (!Regex.IsMatch(prefix, "^" + keywordPattern + @"\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return null;
        }

        var items = new List<Doc>();
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            if (index > 0)
            {
                var previous = elements[index - 1];
                var separator = source.Substring(previous.StartOffset + previous.FragmentLength,
                    element.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!Regex.IsMatch(separator, @"^\s*,\s*$", RegexOptions.CultureInvariant))
                {
                    return null;
                }

                items.Add(new TextDoc(","));
                items.Add(layout == ClauseItemLayout.OnePerLine
                    ? HardLineDoc.Instance : SoftLineDoc.Instance);
            }

            items.Add(new TextDoc(context.GetOriginalText(element).Trim()));
        }

        var last = elements[elements.Count - 1];
        var tail = source.Substring(last.StartOffset + last.FragmentLength,
            clause.StartOffset + clause.FragmentLength - last.StartOffset - last.FragmentLength);
        if (!string.IsNullOrWhiteSpace(tail))
        {
            return null;
        }

        var normalizedKeyword = Regex.Replace(prefix.Trim(), @"\s+", " ");
        var content = new List<Doc>
        {
            layout == ClauseItemLayout.OnePerLine ? HardLineDoc.Instance : SoftLineDoc.Instance
        };
        content.AddRange(items);
        var list = new IndentDoc(1, new ConcatDoc(content));
        return new ConcatDoc(new Doc[]
        {
            new TextDoc(normalizedKeyword),
            layout == ClauseItemLayout.OnePerLine ? list : new GroupDoc(list)
        });
    }
}
