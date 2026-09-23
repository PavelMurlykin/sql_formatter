using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats the supported, uncomplicated SELECT shape without discarding source trivia.</summary>
internal sealed class BasicSelectDocBuilder : ISqlFragmentDocBuilder
{
    private readonly SelectOptions _options;

    public BasicSelectDocBuilder(SelectOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool Applied { get; private set; }

    public bool CanBuild(TSqlFragment fragment) => fragment is SelectStatement;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        var statement = (SelectStatement)fragment;
        if (statement.QueryExpression is not QuerySpecification query
            || statement.WithCtesAndXmlNamespaces is not null
            || query.SelectElements.Count == 0
            || query.WhereClause is not null
            || query.GroupByClause is not null
            || query.HavingClause is not null
            || query.FromClause?.TableReferences.Count > 1
            || (query.FromClause is not null
                && query.FromClause.TableReferences[0] is not NamedTableReference))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var source = context.ParseResult.Source;
        var first = query.SelectElements[0];
        var prefix = source.Substring(query.StartOffset, first.StartOffset - query.StartOffset);
        if (!Regex.IsMatch(prefix, @"^SELECT\s+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var columns = new List<Doc>();
        for (var index = 0; index < query.SelectElements.Count; index++)
        {
            var element = query.SelectElements[index];
            if (index > 0)
            {
                var previous = query.SelectElements[index - 1];
                var separator = source.Substring(previous.StartOffset + previous.FragmentLength,
                    element.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!Regex.IsMatch(separator, @"^\s*,\s*$", RegexOptions.CultureInvariant))
                {
                    return new TextDoc(context.GetOriginalText(statement));
                }

                columns.Add(new TextDoc(","));
                columns.Add(_options.ColumnLayout == SelectColumnLayout.OnePerLine
                    ? HardLineDoc.Instance : SoftLineDoc.Instance);
            }

            columns.Add(new TextDoc(context.GetOriginalText(element).Trim()));
        }

        var lastElement = query.SelectElements[query.SelectElements.Count - 1];
        var cursor = lastElement.StartOffset + lastElement.FragmentLength;
        var columnParts = new List<Doc> { _options.ColumnLayout == SelectColumnLayout.OnePerLine
            ? HardLineDoc.Instance : SoftLineDoc.Instance };
        columnParts.AddRange(columns);
        var columnDoc = new IndentDoc(1, new ConcatDoc(columnParts));
        var parts = new List<Doc> { new TextDoc(prefix.Trim()),
            _options.ColumnLayout == SelectColumnLayout.OnePerLine
                ? columnDoc : new GroupDoc(columnDoc) };

        if (query.FromClause is not null)
        {
            var between = source.Substring(cursor, query.FromClause.StartOffset - cursor);
            var from = query.FromClause;
            var table = from.TableReferences[0];
            var fromPrefix = source.Substring(from.StartOffset, table.StartOffset - from.StartOffset);
            var fromTail = source.Substring(table.StartOffset + table.FragmentLength,
                from.StartOffset + from.FragmentLength - table.StartOffset - table.FragmentLength);
            if (!string.IsNullOrWhiteSpace(between)
                || !Regex.IsMatch(fromPrefix, @"^FROM\s+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !string.IsNullOrWhiteSpace(fromTail))
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(HardLineDoc.Instance);
            parts.Add(new TextDoc(fromPrefix.Trim()));
            parts.Add(new TextDoc(" "));
            parts.Add(new TextDoc(context.GetOriginalText(table).Trim()));
            cursor = from.StartOffset + from.FragmentLength;
        }

        var queryTail = source.Substring(cursor, query.StartOffset + query.FragmentLength - cursor);
        var statementTail = source.Substring(query.StartOffset + query.FragmentLength,
            statement.StartOffset + statement.FragmentLength - query.StartOffset - query.FragmentLength);
        if (!string.IsNullOrWhiteSpace(queryTail)
            || !Regex.IsMatch(statementTail, @"^\s*;?\s*$", RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        parts.Add(new TextDoc(statementTail.Trim()));
        Applied = true;
        return new ConcatDoc(parts);
    }
}
