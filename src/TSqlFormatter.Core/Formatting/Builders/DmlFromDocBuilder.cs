using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Formats a single supported FROM table reference for DML statements.</summary>
internal sealed class DmlFromDocBuilder
{
    private readonly FormattingOptions _options;

    public DmlFromDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(FromClause clause, SqlDocBuilderContext context)
    {
        if (clause.TableReferences.Count != 1) return null;
        var table = clause.TableReferences[0];
        var source = context.ParseResult.Source;
        var prefix = source.Substring(clause.StartOffset, table.StartOffset - clause.StartOffset);
        var tail = source.Substring(table.StartOffset + table.FragmentLength,
            clause.StartOffset + clause.FragmentLength - table.StartOffset - table.FragmentLength);
        if (!Regex.IsMatch(prefix, @"^FROM\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !string.IsNullOrWhiteSpace(tail)) return null;

        var tableDoc = new FromTableDocBuilder(_options).Build(table, context);
        return tableDoc is null ? null : new ConcatDoc(new Doc[]
        {
            new TextDoc(prefix.Trim()), new TextDoc(" "), tableDoc
        });
    }
}
