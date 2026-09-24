using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats UPDATE target, simple SET assignments, FROM, and WHERE.</summary>
internal sealed class UpdateDocBuilder : ISqlFragmentDocBuilder
{
    private readonly FormattingOptions _options;

    public UpdateDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public bool Applied { get; private set; }

    public bool CanBuild(TSqlFragment fragment) => fragment is UpdateStatement;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        var statement = (UpdateStatement)fragment;
        var spec = statement.UpdateSpecification;
        if (spec?.Target is not NamedTableReference target
            || spec.SetClauses.Count == 0
            || spec.TopRowFilter is not null || statement.WithCtesAndXmlNamespaces is not null
            || (spec.OutputClause is not null && spec.OutputIntoClause is not null)
            || spec.StartOffset != statement.StartOffset)
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var source = context.ParseResult.Source;
        var prefix = source.Substring(spec.StartOffset, target.StartOffset - spec.StartOffset);
        var first = spec.SetClauses[0];
        var setPrefix = source.Substring(target.StartOffset + target.FragmentLength,
            first.StartOffset - target.StartOffset - target.FragmentLength);
        if (!Regex.IsMatch(prefix, @"^UPDATE\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !Regex.IsMatch(setPrefix, @"^\s+SET\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var parts = new List<Doc>
        {
            new TextDoc(prefix.Trim() + " " + context.GetOriginalText(target).Trim()),
            HardLineDoc.Instance,
            new TextDoc(Regex.Match(setPrefix, @"SET", RegexOptions.IgnoreCase).Value)
        };
        var assignments = new List<Doc>();
        for (var index = 0; index < spec.SetClauses.Count; index++)
        {
            var clause = spec.SetClauses[index];
            if (clause is not AssignmentSetClause assignment
                || assignment.Variable is not null
                || assignment.Column is null || assignment.NewValue is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            if (index > 0)
            {
                var previous = spec.SetClauses[index - 1];
                var gap = source.Substring(previous.StartOffset + previous.FragmentLength,
                    clause.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!Regex.IsMatch(gap, @"^\s*,\s*$", RegexOptions.CultureInvariant))
                {
                    return new TextDoc(context.GetOriginalText(statement));
                }
                assignments.Add(new TextDoc(","));
            }

            var column = assignment.Column;
            var value = assignment.NewValue;
            var before = source.Substring(clause.StartOffset, column.StartOffset - clause.StartOffset);
            var op = source.Substring(column.StartOffset + column.FragmentLength,
                value.StartOffset - column.StartOffset - column.FragmentLength);
            var tail = source.Substring(value.StartOffset + value.FragmentLength,
                clause.StartOffset + clause.FragmentLength - value.StartOffset - value.FragmentLength);
            if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(tail)
                || !Regex.IsMatch(op, @"^\s*=\s*$", RegexOptions.CultureInvariant))
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            assignments.Add(new IndentDoc(1, new ConcatDoc(new Doc[]
            {
                HardLineDoc.Instance,
                new TextDoc(context.GetOriginalText(column).Trim() + " = "
                    + context.GetOriginalText(value).Trim())
            })));
        }

        parts.Add(new ConcatDoc(assignments));
        var last = spec.SetClauses[spec.SetClauses.Count - 1];
        var cursor = last.StartOffset + last.FragmentLength;
        var output = (TSqlFragment?)spec.OutputClause ?? spec.OutputIntoClause;
        if (output is not null)
        {
            var leading = new LeadingCommentDocBuilder().Build(cursor, output.StartOffset, context);
            var outputDoc = new OutputDocBuilder(_options).Build(
                spec.OutputClause, spec.OutputIntoClause, context);
            if (leading is null || outputDoc is null)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            parts.Add(leading);
            parts.Add(outputDoc);
            cursor = output.StartOffset + output.FragmentLength;
        }

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
