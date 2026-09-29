namespace TSqlFormatter.Core.Formatting;

internal static class NativeRules
{
    public static RuleValue Get(FormattingOptions options, string key) =>
        options.Rules.Catalog.TryGet(key, out _) ? options.Rules.Get(key)
            : RuleCatalog.Default.Definitions[key].DefaultValue;
}
