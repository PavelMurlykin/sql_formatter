using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds a simple WITH clause with one or more common table expressions.</summary>
internal sealed class CteDocBuilder
{
    private readonly FormattingOptions _options;

    public CteDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(WithCtesAndXmlNamespaces with, SqlDocBuilderContext context)
    {
        if (with.XmlNamespaces is not null
            || with.ChangeTrackingContext is not null
            || with.CommonTableExpressions.Count == 0)
        {
            return null;
        }

        var source = context.ParseResult.Source;
        var ctes = with.CommonTableExpressions;
        var prefix = source.Substring(with.StartOffset, ctes[0].StartOffset - with.StartOffset);
        if (!Regex.IsMatch(prefix, @"^WITH\s+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return null;
        }

        var parts = new List<Doc> { new TextDoc(prefix.Trim()), new TextDoc(" ") };
        for (var index = 0; index < ctes.Count; index++)
        {
            var cte = ctes[index];
            if (cte.QueryExpression is null || cte.ExpressionName is null
                || cte.WithCtesAndXmlNamespaces is not null)
            {
                return null;
            }

            if (index > 0)
            {
                var previous = ctes[index - 1];
                var separator = source.Substring(previous.StartOffset + previous.FragmentLength,
                    cte.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!Regex.IsMatch(separator, @"^\s*,\s*$", RegexOptions.CultureInvariant))
                {
                    return null;
                }

                parts.Add(new TextDoc(","));
                parts.Add(HardLineDoc.Instance);
            }

            var name = cte.ExpressionName;
            var query = cte.QueryExpression;
            if (cte.StartOffset != name.StartOffset)
            {
                return null;
            }

            var header = source.Substring(name.StartOffset + name.FragmentLength,
                query.StartOffset - name.StartOffset - name.FragmentLength);
            var headerPattern = cte.Columns.Count == 0
                ? @"^\s+AS\s*\(\s*$"
                : @"^\s*\([^()]*\)\s+AS\s*\(\s*$";
            var suffix = source.Substring(query.StartOffset + query.FragmentLength,
                cte.StartOffset + cte.FragmentLength - query.StartOffset - query.FragmentLength);
            if (!Regex.IsMatch(header, headerPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !Regex.IsMatch(suffix, @"^\s*\)\s*$", RegexOptions.CultureInvariant))
            {
                return null;
            }

            var queryDoc = new NestedQueryDocBuilder(_options).Build(query, context);
            if (queryDoc is null)
            {
                return null;
            }

            parts.Add(new TextDoc(context.GetOriginalText(name)));
            parts.Add(new TextDoc(" "));
            parts.Add(new TextDoc(header.Trim()));
            parts.Add(HardLineDoc.Instance);
            parts.Add(new IndentDoc(1, queryDoc));
            parts.Add(HardLineDoc.Instance);
            parts.Add(new TextDoc(")"));
        }

        var last = ctes[ctes.Count - 1];
        var tail = source.Substring(last.StartOffset + last.FragmentLength,
            with.StartOffset + with.FragmentLength - last.StartOffset - last.FragmentLength);
        return string.IsNullOrWhiteSpace(tail) ? new ConcatDoc(parts) : null;
    }
}
