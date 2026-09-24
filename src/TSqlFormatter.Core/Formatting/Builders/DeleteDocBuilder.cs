using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats basic DELETE target, optional joined FROM, and WHERE.</summary>
internal sealed class DeleteDocBuilder : ISqlFragmentDocBuilder
{
    private readonly FormattingOptions _options;

    public DeleteDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public bool Applied { get; private set; }

    public bool CanBuild(TSqlFragment fragment) => fragment is DeleteStatement;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        var statement = (DeleteStatement)fragment;
        var spec = statement.DeleteSpecification;
        if (spec?.Target is not NamedTableReference target
            || spec.TopRowFilter is not null || spec.OutputClause is not null
            || spec.OutputIntoClause is not null || statement.WithCtesAndXmlNamespaces is not null
            || spec.StartOffset != statement.StartOffset)
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var source = context.ParseResult.Source;
        var prefix = source.Substring(spec.StartOffset, target.StartOffset - spec.StartOffset);
        if (!Regex.IsMatch(prefix, @"^DELETE\s+(?:FROM\s+)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var parts = new List<Doc>
        {
            new TextDoc(Regex.Replace(prefix.Trim(), @"\s+", " ")
                + " " + context.GetOriginalText(target).Trim())
        };
        var cursor = target.StartOffset + target.FragmentLength;
        if (spec.FromClause is not null)
        {
            var from = spec.FromClause;
            var leading = new LeadingCommentDocBuilder().Build(cursor, from.StartOffset, context);
            var fromDoc = new DmlFromDocBuilder(_options).Build(from, context);
            if (leading is null || fromDoc is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(leading);
            parts.Add(fromDoc);
            cursor = from.StartOffset + from.FragmentLength;
        }

        if (spec.WhereClause is not null)
        {
            var where = spec.WhereClause;
            var leading = new LeadingCommentDocBuilder().Build(cursor, where.StartOffset, context);
            var whereDoc = new WhereDocBuilder(_options).Build(where, context);
            if (leading is null || whereDoc is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(leading);
            parts.Add(whereDoc);
            cursor = where.StartOffset + where.FragmentLength;
        }

        var specTail = source.Substring(cursor,
            spec.StartOffset + spec.FragmentLength - cursor);
        var statementTail = source.Substring(spec.StartOffset + spec.FragmentLength,
            statement.StartOffset + statement.FragmentLength - spec.StartOffset - spec.FragmentLength);
        if (!string.IsNullOrWhiteSpace(specTail)
            || !Regex.IsMatch(statementTail, @"^\s*;?\s*$", RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        parts.Add(new TextDoc(statementTail.Trim()));
        Applied = true;
        return new ConcatDoc(parts);
    }
}
