using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds a parenthesized scalar subquery without discarding source tokens.</summary>
internal sealed class SubqueryDocBuilder
{
    private readonly FormattingOptions _options;

    public SubqueryDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(ScalarSubquery subquery, SqlDocBuilderContext context)
    {
        if (subquery.QueryExpression is null) return null;

        var query = subquery.QueryExpression;
        var source = context.ParseResult.Source;
        var prefix = source.Substring(subquery.StartOffset, query.StartOffset - subquery.StartOffset);
        var suffix = source.Substring(query.StartOffset + query.FragmentLength,
            subquery.StartOffset + subquery.FragmentLength - query.StartOffset - query.FragmentLength);
        if (!Regex.IsMatch(prefix, @"^\(\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(suffix, @"^\s*\)$", RegexOptions.CultureInvariant))
        {
            return null;
        }

        var inner = new NestedQueryDocBuilder(_options).Build(query, context);
        return inner is null ? null : new ConcatDoc(new Doc[]
        {
            new TextDoc("("),
            HardLineDoc.Instance,
            new IndentDoc(1, inner),
            HardLineDoc.Instance,
            new TextDoc(")")
        });
    }
}
