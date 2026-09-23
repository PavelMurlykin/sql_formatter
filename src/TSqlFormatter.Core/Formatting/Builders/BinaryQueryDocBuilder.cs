using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats supported set operators between SELECT query expressions.</summary>
internal sealed class BinaryQueryDocBuilder
{
    private readonly FormattingOptions _options;

    public BinaryQueryDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(BinaryQueryExpression binary, SqlDocBuilderContext context)
    {
        var left = binary.FirstQueryExpression;
        var right = binary.SecondQueryExpression;
        if (left is null || right is null || binary.OffsetClause is not null
            || binary.ForClause is not null || binary.StartOffset != left.StartOffset)
        {
            return null;
        }

        var operatorPattern = binary.BinaryQueryExpressionType switch
        {
            BinaryQueryExpressionType.Union when binary.All => @"UNION\s+ALL",
            BinaryQueryExpressionType.Union => "UNION",
            BinaryQueryExpressionType.Intersect when !binary.All => "INTERSECT",
            BinaryQueryExpressionType.Except when !binary.All => "EXCEPT",
            _ => null
        };
        if (operatorPattern is null) return null;

        var source = context.ParseResult.Source;
        var separator = source.Substring(left.StartOffset + left.FragmentLength,
            right.StartOffset - left.StartOffset - left.FragmentLength);
        if (!Regex.IsMatch(separator, @"^\s*" + operatorPattern + @"\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;

        var leftDoc = BuildChild(left, context);
        var rightDoc = BuildChild(right, context);
        if (leftDoc is null || rightDoc is null) return null;

        var parts = new List<Doc>
        {
            leftDoc,
            HardLineDoc.Instance,
            new TextDoc(Regex.Replace(separator.Trim(), @"\s+", " ")),
            HardLineDoc.Instance,
            rightDoc
        };
        var cursor = right.StartOffset + right.FragmentLength;
        if (binary.OrderByClause is not null)
        {
            var order = binary.OrderByClause;
            var gap = source.Substring(cursor, order.StartOffset - cursor);
            var orderDoc = order.All ? null : new ListClauseDocBuilder().Build(order,
                order.OrderByElements, @"ORDER\s+BY", _options.Clauses.OrderByLayout, context);
            if (!string.IsNullOrWhiteSpace(gap) || orderDoc is null) return null;
            parts.Add(HardLineDoc.Instance);
            parts.Add(orderDoc);
            cursor = order.StartOffset + order.FragmentLength;
        }

        var tail = source.Substring(cursor,
            binary.StartOffset + binary.FragmentLength - cursor);
        return string.IsNullOrWhiteSpace(tail) ? new ConcatDoc(parts) : null;
    }

    private Doc? BuildChild(QueryExpression query, SqlDocBuilderContext context)
    {
        return query is BinaryQueryExpression binary
            ? Build(binary, context)
            : new NestedQueryDocBuilder(_options).Build(query, context);
    }
}
