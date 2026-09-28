using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Validates and builds a simple CTE prefix before a DML specification.</summary>
internal sealed class DmlCtePrefixDocBuilder
{
    private readonly FormattingOptions _options;

    public DmlCtePrefixDocBuilder(FormattingOptions options) => _options = options;

    public Doc? Build(TSqlStatement statement, WithCtesAndXmlNamespaces? with,
        TSqlFragment specification, SqlDocBuilderContext context)
    {
        if (with is null)
            return statement.StartOffset == specification.StartOffset ? new TextDoc(string.Empty) : null;

        if (statement.StartOffset != with.StartOffset)
            return null;

        var source = context.ParseResult.Source;
        var gap = source.Substring(with.StartOffset + with.FragmentLength,
            specification.StartOffset - with.StartOffset - with.FragmentLength);
        if (!string.IsNullOrWhiteSpace(gap)) return null;

        var withDoc = new CteDocBuilder(_options).Build(with, context);
        return withDoc is null ? null : new ConcatDoc(new Doc[] { withDoc, HardLineDoc.Instance });
    }
}
