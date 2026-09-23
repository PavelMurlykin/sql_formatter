using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats supported JOIN/APPLY chains, preserving table and ON expressions.</summary>
internal sealed class JoinDocBuilder
{
    private readonly FromTableDocBuilder _tables;
    private readonly KeywordCase _keywordCase;
    private readonly FormattingOptions _options;

    public JoinDocBuilder(FromTableDocBuilder tables, FormattingOptions options)
    {
        _tables = tables;
        _options = options;
        _keywordCase = options.Keywords.Case;
    }

    public Doc? Build(JoinTableReference join, SqlDocBuilderContext context)
    {
        if (join.FirstTableReference is null || join.SecondTableReference is null)
        {
            return null;
        }

        var first = join.FirstTableReference;
        var second = join.SecondTableReference;
        var left = _tables.Build(first, context);
        var right = _tables.Build(second, context);
        if (left is null || right is null || join.StartOffset != first.StartOffset)
        {
            return null;
        }

        var source = context.ParseResult.Source;
        var separator = source.Substring(first.StartOffset + first.FragmentLength,
            second.StartOffset - first.StartOffset - first.FragmentLength);
        var end = join.StartOffset + join.FragmentLength;
        var secondEnd = second.StartOffset + second.FragmentLength;

        if (join is QualifiedJoin qualified)
        {
            if (qualified.JoinHint != JoinHint.None || qualified.SearchCondition is null
                || !Regex.IsMatch(separator,
                    @"^\s*(?:(?:INNER|LEFT(?:\s+OUTER)?|RIGHT(?:\s+OUTER)?|FULL(?:\s+OUTER)?)\s+)?JOIN\s+$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return null;
            }

            var condition = qualified.SearchCondition;
            var onPrefix = source.Substring(secondEnd, condition.StartOffset - secondEnd);
            var tail = source.Substring(condition.StartOffset + condition.FragmentLength,
                end - condition.StartOffset - condition.FragmentLength);
            var expectedOnPrefix = WhereDocBuilder.StartsWithExists(condition)
                ? @"^\s+ON\s+EXISTS\s+$" : @"^\s+ON\s+$";
            if (!Regex.IsMatch(onPrefix, expectedOnPrefix,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !string.IsNullOrWhiteSpace(tail))
            {
                return null;
            }

            var conditionDoc = new WhereDocBuilder(_options).BuildCondition(condition, context)
                ?? new TextDoc(context.GetOriginalText(condition).Trim());
            return new ConcatDoc(new Doc[]
            {
                left, HardLineDoc.Instance,
                new TextDoc(Normalize(separator)), new TextDoc(" "), right,
                HardLineDoc.Instance,
                new IndentDoc(1, new ConcatDoc(new Doc[]
                {
                    new TextDoc(Regex.Match(onPrefix, @"ON", RegexOptions.IgnoreCase).Value),
                    new TextDoc(" "), conditionDoc
                }))
            });
        }

        if (join is UnqualifiedJoin
            && Regex.IsMatch(separator, @"^\s*(?:CROSS\s+JOIN|CROSS\s+APPLY|OUTER\s+APPLY)\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && string.IsNullOrWhiteSpace(source.Substring(secondEnd, end - secondEnd)))
        {
            return new ConcatDoc(new Doc[]
            {
                left, HardLineDoc.Instance,
                new TextDoc(Normalize(separator)), new TextDoc(" "), right
            });
        }

        return null;
    }

    private string Normalize(string text)
    {
        var normalized = Regex.Replace(text.Trim(), @"\s+", " ");
        return _keywordCase switch
        {
            KeywordCase.Upper => normalized.ToUpperInvariant(),
            KeywordCase.Lower => normalized.ToLowerInvariant(),
            _ => normalized
        };
    }
}
