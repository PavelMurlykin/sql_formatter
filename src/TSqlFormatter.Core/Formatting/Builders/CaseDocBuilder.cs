using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats simple and searched CASE expressions with one branch per block.</summary>
internal sealed class CaseDocBuilder
{
    public Doc? Build(CaseExpression expression, SqlDocBuilderContext context)
    {
        IList<WhenClause> clauses;
        ScalarExpression? input = null;
        switch (expression)
        {
            case SimpleCaseExpression simple:
                clauses = simple.WhenClauses.Cast<WhenClause>().ToList();
                input = simple.InputExpression;
                if (input is null) return null;
                break;
            case SearchedCaseExpression searched:
                clauses = searched.WhenClauses.Cast<WhenClause>().ToList();
                break;
            default:
                return null;
        }

        if (clauses.Count == 0) return null;
        var source = context.ParseResult.Source;
        var firstStart = input?.StartOffset ?? clauses[0].StartOffset;
        var prefix = source.Substring(expression.StartOffset, firstStart - expression.StartOffset);
        if (!Regex.IsMatch(prefix, input is null ? @"^CASE\s*$" : @"^CASE\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;

        var header = prefix.Trim();
        if (input is not null)
        {
            var gap = source.Substring(input.StartOffset + input.FragmentLength,
                clauses[0].StartOffset - input.StartOffset - input.FragmentLength);
            if (!string.IsNullOrWhiteSpace(gap)) return null;
            header += " " + context.GetOriginalText(input).Trim();
        }

        var branches = new List<Doc>();
        for (var index = 0; index < clauses.Count; index++)
        {
            var clause = clauses[index];
            if (index > 0)
            {
                var previous = clauses[index - 1];
                var gap = source.Substring(previous.StartOffset + previous.FragmentLength,
                    clause.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!string.IsNullOrWhiteSpace(gap)) return null;
            }

            TSqlFragment? condition = clause switch
            {
                SimpleWhenClause simpleWhen => simpleWhen.WhenExpression,
                SearchedWhenClause searchedWhen => searchedWhen.WhenExpression,
                _ => null
            };
            var then = clause.ThenExpression;
            if (condition is null || then is null) return null;

            var whenPrefix = source.Substring(clause.StartOffset,
                condition.StartOffset - clause.StartOffset);
            var thenPrefix = source.Substring(condition.StartOffset + condition.FragmentLength,
                then.StartOffset - condition.StartOffset - condition.FragmentLength);
            var tail = source.Substring(then.StartOffset + then.FragmentLength,
                clause.StartOffset + clause.FragmentLength - then.StartOffset - then.FragmentLength);
            if (!Regex.IsMatch(whenPrefix, @"^WHEN\s+$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !Regex.IsMatch(thenPrefix, @"^\s+THEN\s+$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !string.IsNullOrWhiteSpace(tail)) return null;

            branches.Add(HardLineDoc.Instance);
            branches.Add(new TextDoc(whenPrefix.Trim() + " " + context.GetOriginalText(condition).Trim()));
            branches.Add(new IndentDoc(1, new ConcatDoc(new Doc[]
            {
                HardLineDoc.Instance,
                new TextDoc(thenPrefix.Trim() + " " + context.GetOriginalText(then).Trim())
            })));
        }

        var last = clauses[clauses.Count - 1];
        var cursor = last.StartOffset + last.FragmentLength;
        if (expression.ElseExpression is not null)
        {
            var otherwise = expression.ElseExpression;
            var elsePrefix = source.Substring(cursor, otherwise.StartOffset - cursor);
            if (!Regex.IsMatch(elsePrefix, @"^\s+ELSE\s+$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;
            branches.Add(HardLineDoc.Instance);
            branches.Add(new TextDoc(elsePrefix.Trim() + " " + context.GetOriginalText(otherwise).Trim()));
            cursor = otherwise.StartOffset + otherwise.FragmentLength;
        }

        var end = source.Substring(cursor,
            expression.StartOffset + expression.FragmentLength - cursor);
        if (!Regex.IsMatch(end, @"^\s+END$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;

        return new ConcatDoc(new Doc[]
        {
            new TextDoc(header),
            new IndentDoc(1, new ConcatDoc(branches)),
            HardLineDoc.Instance,
            new TextDoc(end.Trim())
        });
    }
}
