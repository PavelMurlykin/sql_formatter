using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats basic INSERT column lists, VALUES rows, and SELECT sources.</summary>
internal sealed class InsertDocBuilder : ISqlFragmentDocBuilder
{
    private readonly FormattingOptions _options;

    public InsertDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public bool Applied { get; private set; }

    public bool CanBuild(TSqlFragment fragment) => fragment is InsertStatement;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        var statement = (InsertStatement)fragment;
        var spec = statement.InsertSpecification;
        var target = spec?.Target;
        var source = spec?.InsertSource;
        if (spec is null || target is not NamedTableReference || source is null
            || spec.TopRowFilter is not null || spec.OutputClause is not null
            || spec.OutputIntoClause is not null || statement.WithCtesAndXmlNamespaces is not null
            || statement.StartOffset != spec.StartOffset)
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var sql = context.ParseResult.Source;
        var prefix = sql.Substring(spec.StartOffset, target.StartOffset - spec.StartOffset);
        if (!Regex.IsMatch(prefix, @"^INSERT\s+(?:INTO\s+)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var header = Regex.Replace(prefix.Trim(), @"\s+", " ")
            + " " + context.GetOriginalText(target).Trim();
        var cursor = target.StartOffset + target.FragmentLength;
        if (spec.Columns.Count > 0)
        {
            var columns = spec.Columns;
            var opening = sql.Substring(cursor, columns[0].StartOffset - cursor);
            if (!Regex.IsMatch(opening, @"^\s*\(\s*$", RegexOptions.CultureInvariant))
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            var names = new List<string>();
            for (var index = 0; index < columns.Count; index++)
            {
                var column = columns[index];
                if (index > 0)
                {
                    var previous = columns[index - 1];
                    var gap = sql.Substring(previous.StartOffset + previous.FragmentLength,
                        column.StartOffset - previous.StartOffset - previous.FragmentLength);
                    if (!Regex.IsMatch(gap, @"^\s*,\s*$", RegexOptions.CultureInvariant))
                    {
                        return new TextDoc(context.GetOriginalText(statement));
                    }
                }

                names.Add(context.GetOriginalText(column).Trim());
            }

            var last = columns[columns.Count - 1];
            cursor = last.StartOffset + last.FragmentLength;
            var closing = sql.Substring(cursor, source.StartOffset - cursor);
            if (!Regex.IsMatch(closing, @"^\s*\)\s*$", RegexOptions.CultureInvariant))
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            header += " (" + string.Join(", ", names) + ")";
        }
        else if (!string.IsNullOrWhiteSpace(sql.Substring(cursor, source.StartOffset - cursor)))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        Doc? sourceDoc = source switch
        {
            ValuesInsertSource values => BuildValues(values, context),
            SelectInsertSource select => BuildSelect(select, context),
            _ => null
        };
        var sourceTail = sql.Substring(source.StartOffset + source.FragmentLength,
            spec.StartOffset + spec.FragmentLength - source.StartOffset - source.FragmentLength);
        var statementTail = sql.Substring(spec.StartOffset + spec.FragmentLength,
            statement.StartOffset + statement.FragmentLength - spec.StartOffset - spec.FragmentLength);
        if (sourceDoc is null || !string.IsNullOrWhiteSpace(sourceTail)
            || !Regex.IsMatch(statementTail, @"^\s*;?\s*$", RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        Applied = true;
        return new ConcatDoc(new Doc[]
        {
            new TextDoc(header), HardLineDoc.Instance, sourceDoc,
            new TextDoc(statementTail.Trim())
        });
    }

    private static Doc? BuildValues(ValuesInsertSource values, SqlDocBuilderContext context)
    {
        if (values.IsDefaultValues || values.RowValues.Count == 0) return null;
        var sql = context.ParseResult.Source;
        var rows = values.RowValues;
        var prefix = sql.Substring(values.StartOffset, rows[0].StartOffset - values.StartOffset);
        if (!Regex.IsMatch(prefix, @"^VALUES\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;

        var parts = new List<Doc> { new TextDoc(prefix.Trim()) };
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (index > 0)
            {
                var previous = rows[index - 1];
                var gap = sql.Substring(previous.StartOffset + previous.FragmentLength,
                    row.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!Regex.IsMatch(gap, @"^\s*,\s*$", RegexOptions.CultureInvariant)) return null;
                parts.Add(new TextDoc(","));
            }

            var rowText = BuildRow(row, context);
            if (rowText is null) return null;
            parts.Add(new IndentDoc(1, new ConcatDoc(new Doc[]
            {
                HardLineDoc.Instance,
                new TextDoc(rowText)
            })));
        }

        var lastRow = rows[rows.Count - 1];
        var tail = sql.Substring(lastRow.StartOffset + lastRow.FragmentLength,
            values.StartOffset + values.FragmentLength - lastRow.StartOffset - lastRow.FragmentLength);
        return string.IsNullOrWhiteSpace(tail) ? new ConcatDoc(parts) : null;
    }

    private static string? BuildRow(RowValue row, SqlDocBuilderContext context)
    {
        var values = row.ColumnValues;
        if (values.Count == 0) return null;
        var sql = context.ParseResult.Source;
        var opening = sql.Substring(row.StartOffset, values[0].StartOffset - row.StartOffset);
        if (!Regex.IsMatch(opening, @"^\(\s*$", RegexOptions.CultureInvariant)) return null;

        var items = new List<string>();
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (index > 0)
            {
                var previous = values[index - 1];
                var gap = sql.Substring(previous.StartOffset + previous.FragmentLength,
                    value.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!Regex.IsMatch(gap, @"^\s*,\s*$", RegexOptions.CultureInvariant)) return null;
            }

            items.Add(context.GetOriginalText(value).Trim());
        }

        var last = values[values.Count - 1];
        var closing = sql.Substring(last.StartOffset + last.FragmentLength,
            row.StartOffset + row.FragmentLength - last.StartOffset - last.FragmentLength);
        return Regex.IsMatch(closing, @"^\s*\)$", RegexOptions.CultureInvariant)
            ? "(" + string.Join(", ", items) + ")" : null;
    }

    private Doc? BuildSelect(SelectInsertSource source, SqlDocBuilderContext context)
    {
        var select = source.Select;
        if (select is null || select.StartOffset != source.StartOffset
            || select.FragmentLength != source.FragmentLength)
        {
            return null;
        }

        return new NestedQueryDocBuilder(_options).Build(select, context);
    }
}
