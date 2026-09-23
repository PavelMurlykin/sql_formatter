using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds basic WHERE comparisons and AND/OR trees from AST spans.</summary>
internal sealed class WhereDocBuilder
{
    private readonly FormattingOptions _options;

    public WhereDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(WhereClause where, SqlDocBuilderContext context)
    {
        return where.SearchCondition is null ? null
            : BuildClause(where, where.SearchCondition, @"WHERE", context);
    }

    public Doc? Build(HavingClause having, SqlDocBuilderContext context)
    {
        return having.SearchCondition is null ? null
            : BuildClause(having, having.SearchCondition, @"HAVING", context);
    }

    private Doc? BuildClause(
        TSqlFragment clause,
        BooleanExpression expression,
        string keyword,
        SqlDocBuilderContext context)
    {
        if (expression is null)
        {
            return null;
        }

        var source = context.ParseResult.Source;
        var prefix = source.Substring(clause.StartOffset, expression.StartOffset - clause.StartOffset);
        var tail = source.Substring(expression.StartOffset + expression.FragmentLength,
            clause.StartOffset + clause.FragmentLength - expression.StartOffset - expression.FragmentLength);
        var hasLeadingExists = StartsWithExists(expression);
        var expectedPrefix = "^" + keyword + (hasLeadingExists ? @"\s+EXISTS\s+$" : @"\s+$");
        if (!Regex.IsMatch(prefix, expectedPrefix,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !string.IsNullOrWhiteSpace(tail))
        {
            return null;
        }

        var condition = BuildCondition(expression, context);
        var originalKeyword = Regex.Match(prefix, "^" + keyword,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Value;
        return condition is null ? null : new ConcatDoc(new Doc[]
        {
            new TextDoc(originalKeyword),
            new IndentDoc(1, new ConcatDoc(new Doc[] { HardLineDoc.Instance, condition }))
        });
    }

    internal Doc? BuildCondition(BooleanExpression expression, SqlDocBuilderContext context)
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

            var leftDoc = BuildScalar(left, context);
            var rightDoc = BuildScalar(right, context);
            return leftDoc is null || rightDoc is null ? null : new ConcatDoc(new Doc[]
            {
                leftDoc, new TextDoc(" " + op.Trim() + " "), rightDoc
            });
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
                || !Regex.IsMatch(op, StartsWithExists(right)
                        ? @"^\s*(?:AND|OR)\s+EXISTS\s*$" : @"^\s*(?:AND|OR)\s*$",
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
                leftDoc, HardLineDoc.Instance,
                new TextDoc(Regex.Match(op, @"AND|OR", RegexOptions.IgnoreCase).Value),
                new TextDoc(" "), rightDoc
            });
        }

        if (expression is BooleanParenthesisExpression parenthesis
            && parenthesis.Expression is not null)
        {
            var inner = parenthesis.Expression;
            var prefix = source.Substring(expression.StartOffset, inner.StartOffset - expression.StartOffset);
            var suffix = source.Substring(inner.StartOffset + inner.FragmentLength,
                expression.StartOffset + expression.FragmentLength - inner.StartOffset - inner.FragmentLength);
            if (!Regex.IsMatch(prefix, StartsWithExists(inner)
                    ? @"^\(\s*EXISTS\s+$" : @"^\(\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !Regex.IsMatch(suffix, @"^\s*\)$", RegexOptions.CultureInvariant))
            {
                return null;
            }

            var innerDoc = BuildCondition(inner, context);
            return innerDoc is null ? null : new ConcatDoc(new Doc[]
            {
                new TextDoc("("),
                new IndentDoc(1, new ConcatDoc(new Doc[] { HardLineDoc.Instance, innerDoc })),
                HardLineDoc.Instance,
                new TextDoc(")")
            });
        }

        if (expression is ExistsPredicate exists && exists.Subquery is not null)
        {
            var subquery = exists.Subquery;
            var prefix = source.Substring(expression.StartOffset,
                subquery.StartOffset - expression.StartOffset);
            var tail = source.Substring(subquery.StartOffset + subquery.FragmentLength,
                expression.StartOffset + expression.FragmentLength - subquery.StartOffset - subquery.FragmentLength);
            if (!string.IsNullOrWhiteSpace(prefix) || !string.IsNullOrWhiteSpace(tail))
            {
                return null;
            }

            var queryDoc = new SubqueryDocBuilder(_options).Build(subquery, context);
            var leadingText = source.Substring(0, expression.StartOffset);
            var existsKeyword = Regex.Match(leadingText, @"\bEXISTS\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Value.TrimEnd();
            if (existsKeyword.Length == 0) return null;
            return queryDoc is null ? null : new ConcatDoc(new Doc[]
            {
                new TextDoc(existsKeyword), new TextDoc(" "), queryDoc
            });
        }

        if (expression is InPredicate inPredicate
            && inPredicate.Expression is not null
            && inPredicate.Subquery is not null)
        {
            var value = inPredicate.Expression;
            var subquery = inPredicate.Subquery;
            var prefix = source.Substring(expression.StartOffset, value.StartOffset - expression.StartOffset);
            var op = source.Substring(value.StartOffset + value.FragmentLength,
                subquery.StartOffset - value.StartOffset - value.FragmentLength);
            var tail = source.Substring(subquery.StartOffset + subquery.FragmentLength,
                expression.StartOffset + expression.FragmentLength - subquery.StartOffset - subquery.FragmentLength);
            if (!string.IsNullOrWhiteSpace(prefix)
                || !Regex.IsMatch(op, @"^\s*(?:NOT\s+)?IN\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !string.IsNullOrWhiteSpace(tail))
            {
                return null;
            }

            var queryDoc = new SubqueryDocBuilder(_options).Build(subquery, context);
            return queryDoc is null ? null : new ConcatDoc(new Doc[]
            {
                new TextDoc(context.GetOriginalText(value).Trim()),
                new TextDoc(" " + Regex.Replace(op.Trim(), @"\s+", " ") + " "),
                queryDoc
            });
        }

        return null;
    }

    private Doc? BuildScalar(ScalarExpression expression, SqlDocBuilderContext context)
    {
        return expression is ScalarSubquery subquery
            ? new SubqueryDocBuilder(_options).Build(subquery, context)
            : new TextDoc(context.GetOriginalText(expression).Trim());
    }

    internal static bool StartsWithExists(BooleanExpression expression)
    {
        return expression switch
        {
            ExistsPredicate => true,
            BooleanBinaryExpression binary when binary.FirstExpression is not null
                => StartsWithExists(binary.FirstExpression),
            _ => false
        };
    }
}
