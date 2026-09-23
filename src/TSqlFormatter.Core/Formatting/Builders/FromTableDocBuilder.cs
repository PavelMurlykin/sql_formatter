using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds supported FROM table references while retaining source identifiers.</summary>
internal sealed class FromTableDocBuilder
{
    private readonly FormattingOptions _options;

    public FromTableDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(TableReference table, SqlDocBuilderContext context)
    {
        if (table is JoinTableReference join)
        {
            return new JoinDocBuilder(this, _options.Keywords.Case).Build(join, context);
        }

        if (table is NamedTableReference)
        {
            return new TextDoc(context.GetOriginalText(table).Trim());
        }

        if (table is SchemaObjectFunctionTableReference)
        {
            return new TextDoc(context.GetOriginalText(table).Trim());
        }

        if (table is not QueryDerivedTable derived
            || derived.QueryExpression is null
            || derived.Alias is null
            || derived.Columns.Count != 0)
        {
            return null;
        }

        var source = context.ParseResult.Source;
        var query = derived.QueryExpression;
        var prefix = source.Substring(table.StartOffset, query.StartOffset - table.StartOffset);
        var suffix = source.Substring(query.StartOffset + query.FragmentLength,
            derived.Alias.StartOffset - query.StartOffset - query.FragmentLength);
        var trailing = source.Substring(derived.Alias.StartOffset + derived.Alias.FragmentLength,
            table.StartOffset + table.FragmentLength - derived.Alias.StartOffset - derived.Alias.FragmentLength);
        if (!Regex.IsMatch(prefix, @"^\(\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(suffix, @"^\s*\)\s*(?:AS\s+)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !string.IsNullOrWhiteSpace(trailing))
        {
            return null;
        }

        var innerDoc = BuildQueryExpression(query, context);
        if (innerDoc is null)
        {
            return null;
        }

        return new ConcatDoc(new Doc[]
        {
            new TextDoc("("),
            HardLineDoc.Instance,
            new IndentDoc(1, innerDoc),
            HardLineDoc.Instance,
            new TextDoc(")"),
            new TextDoc(suffix.IndexOf("AS", StringComparison.OrdinalIgnoreCase) >= 0 ? " AS " : " "),
            new TextDoc(context.GetOriginalText(derived.Alias))
        });
    }

    private Doc? BuildQueryExpression(QueryExpression query, SqlDocBuilderContext context)
    {
        if (query is QueryParenthesisExpression parenthesis
            && parenthesis.QueryExpression is not null)
        {
            var child = parenthesis.QueryExpression;
            var source = context.ParseResult.Source;
            var prefix = source.Substring(query.StartOffset, child.StartOffset - query.StartOffset);
            var suffix = source.Substring(child.StartOffset + child.FragmentLength,
                query.StartOffset + query.FragmentLength - child.StartOffset - child.FragmentLength);
            if (!Regex.IsMatch(prefix, @"^\(\s*$", RegexOptions.CultureInvariant)
                || !Regex.IsMatch(suffix, @"^\s*\)$", RegexOptions.CultureInvariant))
            {
                return null;
            }

            var inner = BuildQueryExpression(child, context);
            return inner is null ? null : new ConcatDoc(new Doc[]
            {
                new TextDoc("("),
                HardLineDoc.Instance,
                new IndentDoc(1, inner),
                HardLineDoc.Instance,
                new TextDoc(")")
            });
        }

        if (query is not QuerySpecification)
        {
            return null;
        }

        var nested = new ScriptDomSqlFormatter().Format(context.GetOriginalText(query), _options,
            new FormatRequest(dialect: context.ParseResult.RequestedDialect), context.CancellationToken);
        if (!nested.ParseSucceeded)
        {
            return null;
        }

        var lines = Regex.Split(nested.Text.Trim(), @"\r\n|\n|\r");
        var parts = new List<Doc>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) parts.Add(HardLineDoc.Instance);
            parts.Add(new TextDoc(lines[index]));
        }

        return new ConcatDoc(parts);
    }
}
