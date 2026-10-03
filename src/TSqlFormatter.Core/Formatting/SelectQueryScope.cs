using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;

namespace TSqlFormatter.Core.Formatting;

internal static class SelectQueryScope
{
    public static bool UsesSpaceOffsets(FormattingOptions options) => options.Rules.Overrides.Values.Any(
        value => value.Kind == RuleValueKind.Indent && value.Indent.Style is "relativeSpaces" or "absoluteSpaces");

    public static IEnumerable<SelectStatement> Statements(TSqlScript script, FormattingOptions options,
        CancellationToken cancellationToken) => UsesSpaceOffsets(options)
        ? new SqlFragmentWalker().Walk(script, cancellationToken).OfType<SelectStatement>()
        : script.Batches.SelectMany(batch => batch.Statements).OfType<SelectStatement>();

    public static IEnumerable<QuerySpecification> Queries(TSqlScript script, FormattingOptions options,
        CancellationToken cancellationToken)
    {
        if (!UsesSpaceOffsets(options)) return Statements(script, options, cancellationToken)
            .Select(statement => statement.QueryExpression).OfType<QuerySpecification>();
        var fragments = new SqlFragmentWalker().Walk(script, cancellationToken).ToArray();
        if (NativeRules.Get(options, "subquery.useSelectFormatting").Boolean)
            return fragments.OfType<QuerySpecification>();
        var nested = fragments.Where(fragment => fragment is ScalarSubquery or QueryDerivedTable or CommonTableExpression).ToArray();
        return fragments.OfType<QuerySpecification>().Where(query => !nested.Any(parent =>
            parent.StartOffset <= query.StartOffset && parent.StartOffset + parent.FragmentLength >= query.StartOffset + query.FragmentLength));
    }
}
