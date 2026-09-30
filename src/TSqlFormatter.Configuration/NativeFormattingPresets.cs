using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Original native styles, not conversions or claimed reproductions of third-party profiles.</summary>
public static class NativeFormattingPresets
{
    public static IReadOnlyList<FormattingProfile> Profiles { get; } = Array.AsReadOnly(new[]
    {
        new FormattingProfile("ReadableVertical", "Readable vertical", Create(vertical: true)),
        new FormattingProfile("CompactQueries", "Compact queries", Create(vertical: false))
    });

    private static FormattingOptions Create(bool vertical)
    {
        var rules = new RuleOptions(RuleCatalog.Default);
        foreach (var d in RuleCatalog.Default.Definitions.Values)
        {
            if (d.Key.EndsWith(".stackList", StringComparison.Ordinal) || d.Key.EndsWith(".stackColumns", StringComparison.Ordinal)
                || d.Key.EndsWith(".stackRows", StringComparison.Ordinal))
                rules = rules.With(d.Key, RuleValue.FromChoice(vertical ? "on" : "off"));
            if (!vertical && d.Key.EndsWith(".whenFitsMargin", StringComparison.Ordinal))
                rules = rules.With(d.Key, RuleValue.FromBoolean(true));
        }
        rules = rules.With("subquery.useSelectFormatting", RuleValue.FromBoolean(false));
        foreach (var key in new[] { "select.list.breakBeforeFirstColumn", "execute.parameters.breakBefore",
                     "declare.variables.breakAfter", "routine.parameters.breakAfterOpen", "view.columns.breakAfterOpen",
                     "createTable.columns.breakAfterOpen" })
            rules = rules.With(key, RuleValue.FromChoice(vertical ? "always" : "never"));
        if (vertical)
        {
            foreach (var key in new[] { "select.from.breakBefore", "select.where.breakBefore", "select.orderBy.breakBefore",
                         "select.groupBy.breakBefore", "routine.body.breakBeforeAs", "routine.body.breakBefore",
                         "view.query.breakAfterAs", "trigger.body.breakAfterAs", "labels.breakAfter" })
                rules = rules.With(key, RuleValue.FromChoice("always"));
        }
        return FormattingOptions.Default.With(general: new GeneralOptions(vertical ? 100 : 120),
            select: new SelectOptions(vertical ? SelectColumnLayout.OnePerLine : SelectColumnLayout.Auto),
            clauses: new QueryClauseOptions(vertical ? ClauseItemLayout.OnePerLine : ClauseItemLayout.Auto,
                vertical ? ClauseItemLayout.OnePerLine : ClauseItemLayout.Auto),
            alignment: new AlignmentOptions(vertical, vertical, vertical), rules: rules);
    }
}
