using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Builds supported FROM table references while retaining source identifiers.</summary>
internal sealed class FromTableDocBuilder
{
    private readonly FormattingOptions _options;

    public FromTableDocBuilder(FormattingOptions options)
    {
        _options = options;
    }

    public Doc? Build(TableReference table, SqlDocBuilderContext context)
    {
        if (table is NamedTableReference)
        {
            return new TextDoc(context.GetOriginalText(table).Trim());
        }

        if (table is not QueryDerivedTable derived
            || derived.QueryExpression is not QuerySpecification
            || derived.Alias is null
            || derived.Columns.Count != 0)
        {
            return null;
        }

        var source = context.ParseResult.Source;
        var query = derived.QueryExpression;
        var prefix = source.Substring(table.StartOffset, query.StartOffset - table.StartOffset);
        var suffix = source.Substring(query.StartOffset + query.FragmentLength,
            derived.Alias.StartOffset - query.StartOffset - query.FragmentLength);
        var trailing = source.Substring(derived.Alias.StartOffset + derived.Alias.FragmentLength,
            table.StartOffset + table.FragmentLength - derived.Alias.StartOffset - derived.Alias.FragmentLength);
        if (!Regex.IsMatch(prefix, @"^\(\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(suffix, @"^\s*\)\s*(?:AS\s+)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !string.IsNullOrWhiteSpace(trailing))
        {
            return null;
        }

        var querySource = context.GetOriginalText(query);
        var nested = new ScriptDomSqlFormatter().Format(querySource, _options,
            new FormatRequest(dialect: context.ParseResult.RequestedDialect), context.CancellationToken);
        if (!nested.ParseSucceeded)
        {
            return null;
        }

        var lines = Regex.Split(nested.Text.Trim(), @"\r\n|\n|\r");
        var inner = new List<Doc>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) inner.Add(HardLineDoc.Instance);
            inner.Add(new TextDoc(lines[index]));
        }

        return new ConcatDoc(new Doc[]
        {
            new TextDoc("("),
            HardLineDoc.Instance,
            new IndentDoc(1, new ConcatDoc(inner)),
            HardLineDoc.Instance,
            new TextDoc(")"),
            new TextDoc(suffix.IndexOf("AS", StringComparison.OrdinalIgnoreCase) >= 0 ? " AS " : " "),
            new TextDoc(context.GetOriginalText(derived.Alias))
        });
    }
}
