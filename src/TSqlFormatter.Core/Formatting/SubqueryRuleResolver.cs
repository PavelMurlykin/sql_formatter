namespace TSqlFormatter.Core.Formatting;

/// <summary>Separates v2 SELECT overrides from independently configured nested queries.</summary>
internal static class SubqueryRuleResolver
{
    public static FormattingOptions ForNested(FormattingOptions options)
    {
        if (NativeRules.Get(options, "subquery.useSelectFormatting").Boolean) return options;
        var catalog = options.Rules.Catalog;
        var values = options.Rules.Overrides
            .Where(pair => !pair.Key.StartsWith("select.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in options.Rules.Overrides)
        {
            if (!pair.Key.StartsWith("subquery.", StringComparison.Ordinal)) continue;
            var target = "select." + pair.Key.Substring("subquery.".Length);
            if (catalog.TryGet(target, out var descriptor) && descriptor is not null
                && descriptor.Accepts(pair.Value)) values[target] = pair.Value;
        }
        return options.With(rules: new RuleOptions(catalog, values));
    }
}
