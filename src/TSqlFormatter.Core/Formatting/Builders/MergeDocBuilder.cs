using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats basic MERGE source, ON predicate, actions, and OUTPUT.</summary>
internal sealed class MergeDocBuilder : ISqlFragmentDocBuilder
{
    private readonly FormattingOptions _options;

    public MergeDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public bool Applied { get; private set; }

    public bool CanBuild(TSqlFragment fragment) => fragment is MergeStatement;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        var statement = (MergeStatement)fragment;
        var spec = statement.MergeSpecification;
        if (spec?.Target is not NamedTableReference target
            || spec.TableReference is null || spec.SearchCondition is null
            || spec.ActionClauses.Count == 0 || spec.TopRowFilter is not null
            || statement.WithCtesAndXmlNamespaces is not null
            || (spec.OutputClause is not null && spec.OutputIntoClause is not null)
            || spec.StartOffset != statement.StartOffset)
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var source = context.ParseResult.Source;
        var prefix = source.Substring(spec.StartOffset, target.StartOffset - spec.StartOffset);
        if (!Regex.IsMatch(prefix, @"^MERGE\s+(?:INTO\s+)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var header = CaseKeywords(Regex.Replace(prefix.Trim(), @"\s+", " "))
            + " " + context.GetOriginalText(target).Trim();
        var cursor = target.StartOffset + target.FragmentLength;
        if (spec.TableAlias is not null)
        {
            var alias = spec.TableAlias;
            var aliasPrefix = source.Substring(cursor, alias.StartOffset - cursor);
            if (!Regex.IsMatch(aliasPrefix, @"^\s+(?:AS\s+)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            header += " " + CaseKeywords(Regex.Replace(aliasPrefix.Trim(), @"\s+", " "));
            if (aliasPrefix.IndexOf("AS", StringComparison.OrdinalIgnoreCase) >= 0) header += " ";
            header += context.GetOriginalText(alias);
            cursor = alias.StartOffset + alias.FragmentLength;
        }

        var table = spec.TableReference;
        var usingPrefix = source.Substring(cursor, table.StartOffset - cursor);
        if (!Regex.IsMatch(usingPrefix, @"^\s+USING\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var tableDoc = new FromTableDocBuilder(_options).Build(table, context);
        if (tableDoc is null) return new TextDoc(context.GetOriginalText(statement));
        var condition = spec.SearchCondition;
        var onPrefix = source.Substring(table.StartOffset + table.FragmentLength,
            condition.StartOffset - table.StartOffset - table.FragmentLength);
        var conditionDoc = new WhereDocBuilder(_options).BuildCondition(condition, context);
        if (!Regex.IsMatch(onPrefix, @"^\s+ON\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || conditionDoc is null)
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        var parts = new List<Doc>
        {
            new TextDoc(header), HardLineDoc.Instance,
            new TextDoc(CaseKeywords(usingPrefix.Trim())), new TextDoc(" "), tableDoc,
            HardLineDoc.Instance,
            new TextDoc(CaseKeywords(onPrefix.Trim())), new TextDoc(" "), conditionDoc
        };
        cursor = condition.StartOffset + condition.FragmentLength;
        foreach (var clause in spec.ActionClauses)
        {
            if (clause.Action is null || clause.SearchCondition is not null
                || clause.StartOffset != clause.Action.StartOffset)
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            var gap = source.Substring(cursor, clause.StartOffset - cursor);
            var conditionPattern = clause.Condition.ToString() switch
            {
                "Matched" => @"WHEN\s+MATCHED",
                "NotMatched" => @"WHEN\s+NOT\s+MATCHED(?:\s+BY\s+TARGET)?",
                "NotMatchedBySource" => @"WHEN\s+NOT\s+MATCHED\s+BY\s+SOURCE",
                _ => null
            };
            if (conditionPattern is null || !Regex.IsMatch(gap,
                    @"^\s+" + conditionPattern + @"\s+THEN\s+$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return new TextDoc(context.GetOriginalText(statement));
            }

            var actionDoc = BuildAction(clause.Action, context);
            if (actionDoc is null) return new TextDoc(context.GetOriginalText(statement));
            parts.Add(HardLineDoc.Instance);
            parts.Add(new TextDoc(CaseKeywords(Regex.Replace(gap.Trim(), @"\s+", " "))));
            parts.Add(new IndentDoc(1, new ConcatDoc(new Doc[]
            {
                HardLineDoc.Instance, actionDoc
            })));
            cursor = clause.StartOffset + clause.FragmentLength;
        }

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

        var specTail = source.Substring(cursor,
            spec.StartOffset + spec.FragmentLength - cursor);
        var statementTail = source.Substring(spec.StartOffset + spec.FragmentLength,
            statement.StartOffset + statement.FragmentLength - spec.StartOffset - spec.FragmentLength);
        if (!string.IsNullOrWhiteSpace(specTail)
            || !Regex.IsMatch(statementTail, @"^\s*;\s*$", RegexOptions.CultureInvariant))
        {
            return new TextDoc(context.GetOriginalText(statement));
        }

        parts.Add(new TextDoc(";"));
        Applied = true;
        return new ConcatDoc(parts);
    }

    private Doc? BuildAction(MergeAction action, SqlDocBuilderContext context)
    {
        return action switch
        {
            UpdateMergeAction update => BuildUpdate(update, context),
            InsertMergeAction insert => BuildInsert(insert, context),
            DeleteMergeAction delete => BuildDelete(delete, context),
            _ => null
        };
    }

    private Doc? BuildUpdate(UpdateMergeAction action, SqlDocBuilderContext context)
    {
        if (action.SetClauses.Count == 0) return null;
        var source = context.ParseResult.Source;
        var clauses = action.SetClauses;
        var prefix = source.Substring(action.StartOffset,
            clauses[0].StartOffset - action.StartOffset);
        if (!Regex.IsMatch(prefix, @"^UPDATE\s+SET\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;

        var parts = new List<Doc>
        {
            new TextDoc(CaseKeywords(Regex.Replace(prefix.Trim(), @"\s+", " ")))
        };
        for (var index = 0; index < clauses.Count; index++)
        {
            if (clauses[index] is not AssignmentSetClause assignment
                || assignment.Variable is not null
                || assignment.Column is null || assignment.NewValue is null) return null;
            if (index > 0)
            {
                var previous = clauses[index - 1];
                var gap = source.Substring(previous.StartOffset + previous.FragmentLength,
                    assignment.StartOffset - previous.StartOffset - previous.FragmentLength);
                if (!Regex.IsMatch(gap, @"^\s*,\s*$", RegexOptions.CultureInvariant)) return null;
                parts.Add(new TextDoc(","));
            }

            var column = assignment.Column;
            var value = assignment.NewValue;
            var before = source.Substring(assignment.StartOffset,
                column.StartOffset - assignment.StartOffset);
            var op = source.Substring(column.StartOffset + column.FragmentLength,
                value.StartOffset - column.StartOffset - column.FragmentLength);
            var tail = source.Substring(value.StartOffset + value.FragmentLength,
                assignment.StartOffset + assignment.FragmentLength - value.StartOffset - value.FragmentLength);
            if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(tail)
                || !Regex.IsMatch(op, @"^\s*=\s*$", RegexOptions.CultureInvariant)) return null;
            parts.Add(new IndentDoc(1, new ConcatDoc(new Doc[]
            {
                HardLineDoc.Instance,
                new TextDoc(context.GetOriginalText(column).Trim() + " = "
                    + context.GetOriginalText(value).Trim())
            })));
        }

        var last = clauses[clauses.Count - 1];
        var actionTail = source.Substring(last.StartOffset + last.FragmentLength,
            action.StartOffset + action.FragmentLength - last.StartOffset - last.FragmentLength);
        return string.IsNullOrWhiteSpace(actionTail) ? new ConcatDoc(parts) : null;
    }

    private Doc? BuildInsert(InsertMergeAction action, SqlDocBuilderContext context)
    {
        if (action.Source is not ValuesInsertSource values) return null;
        var source = context.ParseResult.Source;
        var columns = action.Columns;
        var firstStart = columns.Count > 0 ? columns[0].StartOffset : values.StartOffset;
        var prefix = source.Substring(action.StartOffset, firstStart - action.StartOffset);
        if (!Regex.IsMatch(prefix, columns.Count > 0 ? @"^INSERT\s*\(\s*$" : @"^INSERT\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;

        var header = CaseKeywords(Regex.Match(prefix, @"^INSERT",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Value);
        if (columns.Count > 0)
        {
            var names = new List<string>();
            for (var index = 0; index < columns.Count; index++)
            {
                var column = columns[index];
                if (index > 0)
                {
                    var previous = columns[index - 1];
                    var gap = source.Substring(previous.StartOffset + previous.FragmentLength,
                        column.StartOffset - previous.StartOffset - previous.FragmentLength);
                    if (!Regex.IsMatch(gap, @"^\s*,\s*$", RegexOptions.CultureInvariant)) return null;
                }
                names.Add(context.GetOriginalText(column).Trim());
            }

            var lastColumn = columns[columns.Count - 1];
            var closing = source.Substring(lastColumn.StartOffset + lastColumn.FragmentLength,
                values.StartOffset - lastColumn.StartOffset - lastColumn.FragmentLength);
            if (!Regex.IsMatch(closing, @"^\s*\)\s*$", RegexOptions.CultureInvariant)) return null;
            header += " (" + string.Join(", ", names) + ")";
        }

        var valuesDoc = InsertDocBuilder.BuildValues(values, context);
        var tail = source.Substring(values.StartOffset + values.FragmentLength,
            action.StartOffset + action.FragmentLength - values.StartOffset - values.FragmentLength);
        return valuesDoc is null || !string.IsNullOrWhiteSpace(tail) ? null
            : new ConcatDoc(new Doc[] { new TextDoc(header), HardLineDoc.Instance, valuesDoc });
    }

    private Doc? BuildDelete(DeleteMergeAction action, SqlDocBuilderContext context)
    {
        var text = context.GetOriginalText(action);
        return Regex.IsMatch(text, @"^DELETE$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? new TextDoc(CaseKeywords(text)) : null;
    }

    private string CaseKeywords(string value)
    {
        return _options.Keywords.Case switch
        {
            KeywordCase.Upper => value.ToUpperInvariant(),
            KeywordCase.Lower => value.ToLowerInvariant(),
            _ => value
        };
    }
}
