using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats the supported, uncomplicated SELECT shape without discarding source trivia.</summary>
internal sealed class BasicSelectDocBuilder : ISqlFragmentDocBuilder
{
    private readonly FormattingOptions _options;

    public BasicSelectDocBuilder(FormattingOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool Applied { get; private set; }

    public bool CanBuild(TSqlFragment fragment) => fragment is SelectStatement;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        var statement = (SelectStatement)fragment;
        if (statement.QueryExpression is not QuerySpecification query
            || query.SelectElements.Count == 0
            || query.OffsetClause is not null
            || query.ForClause is not null
            || query.FromClause?.TableReferences.Count > 1)
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

        var columnDoc = new SelectColumnDocBuilder(_options.Select).Build(query, context);
        if (columnDoc is null)
        {
            return new TextDoc(context.GetOriginalText(statement));
        }
        var lastElement = query.SelectElements[query.SelectElements.Count - 1];
        var cursor = lastElement.StartOffset + lastElement.FragmentLength;
        var parts = new List<Doc>();
        if (statement.WithCtesAndXmlNamespaces is not null)
        {
            var with = statement.WithCtesAndXmlNamespaces;
            var withDoc = new CteDocBuilder(_options).Build(with, context);
            var between = source.Substring(with.StartOffset + with.FragmentLength,
                query.StartOffset - with.StartOffset - with.FragmentLength);
            if (withDoc is null || !string.IsNullOrWhiteSpace(between)
                || statement.StartOffset != with.StartOffset)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(withDoc);
            parts.Add(HardLineDoc.Instance);
        }

        parts.Add(new TextDoc(prefix.Trim()));
        parts.Add(columnDoc);

        if (query.FromClause is not null)
        {
            var leading = new LeadingCommentDocBuilder().Build(cursor, query.FromClause.StartOffset, context);
            var from = query.FromClause;
            var table = from.TableReferences[0];
            var fromPrefix = source.Substring(from.StartOffset, table.StartOffset - from.StartOffset);
            var fromTail = source.Substring(table.StartOffset + table.FragmentLength,
                from.StartOffset + from.FragmentLength - table.StartOffset - table.FragmentLength);
            var tableDoc = new FromTableDocBuilder(_options).Build(table, context);
            if (leading is null
                || !Regex.IsMatch(fromPrefix, @"^FROM\s+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || !string.IsNullOrWhiteSpace(fromTail)
                || tableDoc is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(leading);
            parts.Add(new TextDoc(fromPrefix.Trim()));
            parts.Add(new TextDoc(" "));
            parts.Add(tableDoc);
            cursor = from.StartOffset + from.FragmentLength;
        }

        if (query.WhereClause is not null)
        {
            var where = query.WhereClause;
            var leading = new LeadingCommentDocBuilder().Build(cursor, where.StartOffset, context);
            var whereDoc = new WhereDocBuilder().Build(where, context);
            if (leading is null || whereDoc is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(leading);
            parts.Add(whereDoc);
            cursor = where.StartOffset + where.FragmentLength;
        }

        if (query.GroupByClause is not null)
        {
            var group = query.GroupByClause;
            var leading = new LeadingCommentDocBuilder().Build(cursor, group.StartOffset, context);
            var groupDoc = group.GroupingSpecifications.All(item => item is ExpressionGroupingSpecification)
                ? new ListClauseDocBuilder().Build(group, group.GroupingSpecifications,
                    @"GROUP\s+BY", _options.Clauses.GroupByLayout, context)
                : null;
            if (leading is null || groupDoc is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(leading);
            parts.Add(groupDoc);
            cursor = group.StartOffset + group.FragmentLength;
        }

        if (query.HavingClause is not null)
        {
            var having = query.HavingClause;
            var leading = new LeadingCommentDocBuilder().Build(cursor, having.StartOffset, context);
            var havingDoc = new WhereDocBuilder().Build(having, context);
            if (leading is null || havingDoc is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(leading);
            parts.Add(havingDoc);
            cursor = having.StartOffset + having.FragmentLength;
        }

        if (query.OrderByClause is not null)
        {
            var order = query.OrderByClause;
            var leading = new LeadingCommentDocBuilder().Build(cursor, order.StartOffset, context);
            var orderDoc = order.All ? null : new ListClauseDocBuilder().Build(order,
                order.OrderByElements, @"ORDER\s+BY", _options.Clauses.OrderByLayout, context);
            if (leading is null || orderDoc is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(leading);
            parts.Add(orderDoc);
            cursor = order.StartOffset + order.FragmentLength;
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
