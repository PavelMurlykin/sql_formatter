using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats a nested SELECT, including optional query parentheses.</summary>
internal sealed class NestedQueryDocBuilder
{
    private readonly FormattingOptions _options;

    public NestedQueryDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(QueryExpression query, SqlDocBuilderContext context)
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

            var inner = Build(child, context);
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
