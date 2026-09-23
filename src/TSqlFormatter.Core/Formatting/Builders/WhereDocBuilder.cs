using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds basic WHERE comparisons and AND/OR trees from AST spans.</summary>
internal sealed class WhereDocBuilder
{
    public Doc? Build(WhereClause where, SqlDocBuilderContext context)
    {
        if (where.SearchCondition is null)
        {
            return null;
        }

        var source = context.ParseResult.Source;
        var expression = where.SearchCondition;
        var prefix = source.Substring(where.StartOffset, expression.StartOffset - where.StartOffset);
        var tail = source.Substring(expression.StartOffset + expression.FragmentLength,
            where.StartOffset + where.FragmentLength - expression.StartOffset - expression.FragmentLength);
        if (!Regex.IsMatch(prefix, @"^WHERE\s+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !string.IsNullOrWhiteSpace(tail))
        {
            return null;
        }

        var condition = BuildCondition(expression, context);
        return condition is null ? null : new ConcatDoc(new Doc[]
        {
            new TextDoc(prefix.Trim()),
            new IndentDoc(1, new ConcatDoc(new Doc[] { HardLineDoc.Instance, condition }))
        });
    }

    private static Doc? BuildCondition(BooleanExpression expression, SqlDocBuilderContext context)
    {
        var source = context.ParseResult.Source;
        if (expression is BooleanComparisonExpression comparison
            && comparison.FirstExpression is not null
            && comparison.SecondExpression is not null)
        {
            var left = comparison.FirstExpression;
            var right = comparison.SecondExpression;
            var prefix = source.Substring(expression.StartOffset, left.StartOffset - expression.StartOffset);
            var op = source.Substring(left.StartOffset + left.FragmentLength,
                right.StartOffset - left.StartOffset - left.FragmentLength);
            var tail = source.Substring(right.StartOffset + right.FragmentLength,
                expression.StartOffset + expression.FragmentLength - right.StartOffset - right.FragmentLength);
            if (!string.IsNullOrWhiteSpace(prefix) || !string.IsNullOrWhiteSpace(tail)
                || !Regex.IsMatch(op, @"^\s*(?:=|<>|!=|>=|<=|>|<|!>|!<)\s*$"))
            {
                return null;
            }

            return new TextDoc(context.GetOriginalText(left).Trim() + " " + op.Trim() + " "
                + context.GetOriginalText(right).Trim());
        }

        if (expression is BooleanBinaryExpression binary
            && binary.FirstExpression is not null
            && binary.SecondExpression is not null)
        {
            var left = binary.FirstExpression;
            var right = binary.SecondExpression;
            var prefix = source.Substring(expression.StartOffset, left.StartOffset - expression.StartOffset);
            var op = source.Substring(left.StartOffset + left.FragmentLength,
                right.StartOffset - left.StartOffset - left.FragmentLength);
            var tail = source.Substring(right.StartOffset + right.FragmentLength,
                expression.StartOffset + expression.FragmentLength - right.StartOffset - right.FragmentLength);
            if (!string.IsNullOrWhiteSpace(prefix) || !string.IsNullOrWhiteSpace(tail)
                || !Regex.IsMatch(op, @"^\s*(?:AND|OR)\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return null;
            }

            var leftDoc = BuildCondition(left, context);
            var rightDoc = BuildCondition(right, context);
            if (leftDoc is null || rightDoc is null)
            {
                return null;
            }

            return new ConcatDoc(new Doc[]
            {
                leftDoc, HardLineDoc.Instance, new TextDoc(op.Trim()), new TextDoc(" "), rightDoc
            });
        }

        return null;
    }
}
