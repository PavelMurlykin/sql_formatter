using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Applies independent CTE-header rules to parenthesized CTE query definitions.</summary>
internal static class SubqueryCteLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (NativeRules.Get(options, "subquery.useSelectFormatting").Boolean) return source;
        var catalog = options.Rules.Catalog;
        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        foreach (var pair in options.Rules.Overrides)
        {
            if (!pair.Key.StartsWith("subquery.cte.", StringComparison.Ordinal)) continue;
            var target = "select.cte." + pair.Key.Substring("subquery.cte.".Length);
            if (catalog.TryGet(target, out var descriptor) && descriptor is not null
                && descriptor.Accepts(pair.Value)) values[target] = pair.Value;
        }
        if (values.Count == 0) return source;
        var nestedOptions = options.With(rules: new RuleOptions(catalog, values));
        return SelectTailLayout.ApplySafe(source, nestedOptions, parser, dialect, cancellationToken);
    }
}
