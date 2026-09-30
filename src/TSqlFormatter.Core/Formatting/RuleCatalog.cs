using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Metadata for one native rule; XML identifiers are deliberately not used as keys.</summary>
public sealed class RuleDescriptor
{
    public RuleDescriptor(string key, string scope, RuleValue defaultValue,
        int minimum = int.MinValue, int maximum = int.MaxValue,
        IEnumerable<string>? choices = null, string? dependsOn = null,
        string? dialect = null)
    {
        if (key is null || !Regex.IsMatch(key, @"^[a-z][A-Za-z0-9]*(?:\.[a-z][A-Za-z0-9]*)+$"))
            throw new ArgumentException("Use a dotted native rule key.", nameof(key));
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope is required.", nameof(scope));
        if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum));
        Key = key;
        Scope = scope;
        DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
        Minimum = minimum;
        Maximum = maximum;
        Choices = Array.AsReadOnly((choices ?? Array.Empty<string>()).ToArray());
        DependsOn = dependsOn;
        Dialect = dialect;
        if (!Accepts(defaultValue)) throw new ArgumentException("Invalid default rule value.", nameof(defaultValue));
    }

    public string Key { get; }
    public string Scope { get; }
    public RuleValue DefaultValue { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public IReadOnlyList<string> Choices { get; }
    public string? DependsOn { get; }
    public string? Dialect { get; }

    public bool Accepts(RuleValue value)
    {
        if (value is null || value.Kind != DefaultValue.Kind) return false;
        return value.Kind switch
        {
            RuleValueKind.Integer => value.Integer >= Minimum && value.Integer <= Maximum,
            RuleValueKind.Threshold => value.Threshold.Value >= Minimum && value.Threshold.Value <= Maximum,
            RuleValueKind.Indent => value.Indent.Offset >= Minimum && value.Indent.Offset <= Maximum,
            RuleValueKind.Choice => Choices.Count == 0 || Choices.Contains(value.Choice),
            _ => true
        };
    }
}

public sealed class RuleCatalog
{
    private readonly IReadOnlyDictionary<string, RuleDescriptor> definitions;

    public RuleCatalog(IEnumerable<RuleDescriptor> descriptors)
    {
        if (descriptors is null) throw new ArgumentNullException(nameof(descriptors));
        var map = new Dictionary<string, RuleDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (descriptor is null || map.ContainsKey(descriptor.Key))
                throw new ArgumentException("Null or duplicate rule descriptor.", nameof(descriptors));
            map.Add(descriptor.Key, descriptor);
        }
        foreach (var descriptor in map.Values)
        {
            if (descriptor.DependsOn is not null && !map.ContainsKey(descriptor.DependsOn))
                throw new ArgumentException($"Unknown dependency: {descriptor.DependsOn}", nameof(descriptors));
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = descriptor;
            while (current.DependsOn is not null)
            {
                if (!visited.Add(current.Key))
                    throw new ArgumentException($"Cyclic rule dependency: {descriptor.Key}", nameof(descriptors));
                current = map[current.DependsOn];
            }
        }
        definitions = new ReadOnlyDictionary<string, RuleDescriptor>(map);
    }

    public static RuleCatalog Default { get; } = new(new[]
    {
        CaseRule("textCase.keyword", "inherit"),
        CaseRule("textCase.builtin", "inherit"),
        CaseRule("textCase.dataType", "inherit"),
        CaseRule("textCase.identifier", "preserve"),
        CaseRule("textCase.variable", "preserve"),
        CaseRule("textCase.alias", "preserve"),
        new RuleDescriptor("textCase.formatQuotedIdentifier", "quoted identifier",
            RuleValue.FromBoolean(false)),
        SpacingRule("spacing.arithmeticOperators"),
        SpacingRule("spacing.beforeComma"),
        SpacingRule("spacing.afterComma"),
        SpacingRule("spacing.beforeDot"),
        SpacingRule("spacing.afterDot"),
        SpacingRule("spacing.beforeScopeResolution"),
        SpacingRule("spacing.afterScopeResolution"),
        SpacingRule("spacing.beforeFunctionArguments"),
        SpacingRule("spacing.withinEmptyFunctionArguments"),
        SpacingRule("spacing.withinFunctionArguments"),
        new RuleDescriptor("stackedList.commaPlacement", "vertical list",
            RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "leading", "trailing" }),
        new RuleDescriptor("stackedList.spaceAfterLeadingComma", "vertical list",
            RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "insert", "remove" }),
        new RuleDescriptor("misc.packageDelimiterBlankLine", "batch delimiter",
            RuleValue.FromBoolean(false)),
        new RuleDescriptor("misc.packageDelimiterBlankLineMode", "batch delimiter",
            RuleValue.FromChoice("after"), choices: new[] { "after", "before", "both" },
            dependsOn: "misc.packageDelimiterBlankLine"),
        new RuleDescriptor("select.singleLine.maxWords", "SELECT statement",
            RuleValue.FromThreshold(new ThresholdRule(false, 10)), 0, 10000),
        new RuleDescriptor("select.singleLine.maxCharacters", "SELECT statement",
            RuleValue.FromThreshold(new ThresholdRule(false, 50)), 0, 1000000),
        new RuleDescriptor("select.singleLine.whenFitsMargin", "SELECT statement",
            RuleValue.FromBoolean(false)),
        new RuleDescriptor("select.list.breakBeforeFirstColumn", "SELECT list",
            RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "always", "never" }),
        new RuleDescriptor("select.list.indent", "SELECT list",
            RuleValue.FromIndent(new IndentRule(false, 0, true)), -32, 32),
        new RuleDescriptor("select.list.stackColumns", "SELECT list",
            RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "on", "off" }),
        new RuleDescriptor("select.list.stackMode", "SELECT list",
            RuleValue.FromChoice("onePerLine"), choices: new[] { "onePerLine", "auto" },
            dependsOn: "select.list.stackColumns"),
        IndentDescriptor("select.from.keywordIndent", "FROM keyword"),
        IndentDescriptor("select.from.listIndent", "FROM list"),
        BreakRule("select.from.breakBefore", "FROM keyword"),
        BreakRule("select.from.breakAfter", "FROM keyword"),
        StackRule("select.from.stackList", "FROM list"),
        new RuleDescriptor("select.from.stackMode", "FROM list", RuleValue.FromChoice("onePerLine"),
            choices: new[] { "onePerLine", "auto" }, dependsOn: "select.from.stackList"),
        IndentDescriptor("select.into.keywordIndent", "INTO keyword"),
        IndentDescriptor("select.into.tableIndent", "INTO table"),
        BreakRule("select.into.breakBefore", "INTO keyword"),
        BreakRule("select.into.breakAfter", "INTO keyword"),
        IndentDescriptor("select.join.keywordIndent", "JOIN keyword"),
        IndentDescriptor("select.join.tableIndent", "JOIN table"),
        IndentDescriptor("select.join.onKeywordIndent", "ON keyword"),
        IndentDescriptor("select.join.onConditionIndent", "ON condition"),
        IndentDescriptor("select.join.nestedConditionIndent", "ON nested condition"),
        BreakRule("select.join.breakBefore", "JOIN keyword"),
        BreakRule("select.join.breakAfter", "JOIN keyword"),
        BreakRule("select.join.onBreakBefore", "ON keyword"),
        BreakRule("select.join.onBreakAfter", "ON keyword"),
        WrapRule("select.join.wrapCondition", "ON condition"),
        BreakRule("select.join.wrapBeforeOperator", "ON condition"),
        BreakRule("select.join.wrapAfterOperator", "ON condition"),
        IndentDescriptor("select.where.keywordIndent", "WHERE keyword"),
        IndentDescriptor("select.where.conditionIndent", "WHERE condition"),
        IndentDescriptor("select.where.nestedConditionIndent", "WHERE nested condition"),
        BreakRule("select.where.breakBefore", "WHERE keyword"),
        BreakRule("select.where.breakAfter", "WHERE condition"),
        WrapRule("select.where.wrapCondition", "WHERE condition"),
        BreakRule("select.where.wrapBeforeOperator", "WHERE condition"),
        BreakRule("select.where.wrapAfterOperator", "WHERE condition"),
        IndentDescriptor("select.having.keywordIndent", "HAVING keyword"),
        IndentDescriptor("select.having.conditionIndent", "HAVING condition"),
        IndentDescriptor("select.having.nestedConditionIndent", "HAVING nested condition"),
        BreakRule("select.having.breakBefore", "HAVING keyword"),
        BreakRule("select.having.breakAfter", "HAVING condition"),
        WrapRule("select.having.wrapCondition", "HAVING condition"),
        BreakRule("select.having.wrapBeforeOperator", "HAVING condition"),
        BreakRule("select.having.wrapAfterOperator", "HAVING condition"),
        IndentDescriptor("select.groupBy.keywordIndent", "GROUP BY phrase"),
        IndentDescriptor("select.groupBy.listIndent", "GROUP BY list"),
        BreakRule("select.groupBy.breakBefore", "GROUP BY phrase"),
        BreakRule("select.groupBy.breakAfter", "GROUP BY list"),
        StackRule("select.groupBy.stackList", "GROUP BY list"),
        new RuleDescriptor("select.groupBy.stackMode", "GROUP BY list", RuleValue.FromChoice("onePerLine"),
            choices: new[] { "onePerLine", "auto" }, dependsOn: "select.groupBy.stackList"),
        IndentDescriptor("select.orderBy.keywordIndent", "ORDER BY phrase"),
        IndentDescriptor("select.orderBy.listIndent", "ORDER BY list"),
        BreakRule("select.orderBy.breakBefore", "ORDER BY phrase"),
        BreakRule("select.orderBy.breakAfter", "ORDER BY list"),
        StackRule("select.orderBy.stackList", "ORDER BY list"),
        new RuleDescriptor("select.orderBy.stackMode", "ORDER BY list", RuleValue.FromChoice("onePerLine"),
            choices: new[] { "onePerLine", "auto" }, dependsOn: "select.orderBy.stackList"),
        new RuleDescriptor("select.compute.keywordIndent", "COMPUTE keyword",
            RuleValue.FromIndent(new IndentRule(false, 0, true)), -32, 32, dialect: "Sql2008"),
        new RuleDescriptor("select.compute.expressionIndent", "COMPUTE expression",
            RuleValue.FromIndent(new IndentRule(false, 0, true)), -32, 32, dialect: "Sql2008"),
        new RuleDescriptor("select.compute.breakBefore", "COMPUTE keyword",
            RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "always", "never" }, dialect: "Sql2008"),
        new RuleDescriptor("select.compute.breakAfter", "COMPUTE expression",
            RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "always", "never" }, dialect: "Sql2008"),
        IndentDescriptor("select.cte.columnListIndent", "CTE column list"),
        IndentDescriptor("select.cte.columnBraceIndent", "CTE column list braces"),
        BreakRule("select.cte.breakBeforeColumnOpen", "CTE column list"),
        BreakRule("select.cte.breakAfterColumnOpen", "CTE column list"),
        BreakRule("select.cte.breakBeforeColumnClose", "CTE column list"),
        StackRule("select.cte.stackColumns", "CTE column list"),
        new RuleDescriptor("select.cte.stackMode", "CTE column list", RuleValue.FromChoice("onePerLine"),
            choices: new[] { "onePerLine", "auto" }, dependsOn: "select.cte.stackColumns"),
        IndentDescriptor("select.cte.expressionIndent", "CTE expression"),
        IndentDescriptor("select.cte.subqueryBraceIndent", "CTE subquery braces"),
        BreakRule("select.cte.breakAfterWith", "CTE expression"),
        BreakRule("select.cte.breakBeforeAs", "CTE AS"),
        BreakRule("select.cte.breakAfterAs", "CTE subquery"),
        IndentDescriptor("select.for.keywordIndent", "FOR keyword"),
        IndentDescriptor("select.for.specIndent", "FOR specification"),
        BreakRule("select.for.breakBefore", "FOR keyword"),
        BreakRule("select.for.breakAfterXml", "FOR XML specification"),
        IndentDescriptor("select.option.keywordIndent", "OPTION keyword"),
        IndentDescriptor("select.option.hintsIndent", "OPTION hints"),
        BreakRule("select.option.breakBefore", "OPTION keyword"),
        BreakRule("select.option.breakAfter", "OPTION hints"),
        new RuleDescriptor("subquery.useSelectFormatting", "subquery",
            RuleValue.FromBoolean(true)),
        IndentDescriptor("subquery.indent", "subquery body"),
        BreakRule("subquery.breakBeforeOpen", "subquery braces"),
        BreakRule("subquery.breakAfterOpen", "subquery braces"),
        BreakRule("subquery.breakBeforeClose", "subquery braces"),
        BreakRule("subquery.breakAfterClose", "subquery braces"),
        IndentDescriptor("subquery.list.indent", "subquery SELECT list"),
        BreakRule("subquery.list.breakBeforeFirstColumn", "subquery SELECT list"),
        StackRule("subquery.list.stackColumns", "subquery SELECT list"),
        new RuleDescriptor("subquery.list.stackMode", "subquery SELECT list",
            RuleValue.FromChoice("onePerLine"), choices: new[] { "onePerLine", "auto" },
            dependsOn: "subquery.list.stackColumns")
    }.Concat(new[] { "allAnySomeExists", "cteQueries", "fromList", "inOperator", "other" }
        .SelectMany(SingleLineRules)).ToArray());

    private static RuleDescriptor CaseRule(string key, string defaultValue) => new(key, "token",
        RuleValue.FromChoice(defaultValue), choices: new[] { "inherit", "preserve", "upper", "lower" });
    private static RuleDescriptor SpacingRule(string key) => new(key, "token gap",
        RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "insert", "remove" });
    private static RuleDescriptor IndentDescriptor(string key, string scope) => new(key, scope,
        RuleValue.FromIndent(new IndentRule(false, 0, true)), -32, 32);
    private static RuleDescriptor BreakRule(string key, string scope) => new(key, scope,
        RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "always", "never" });
    private static RuleDescriptor StackRule(string key, string scope) => new(key, scope,
        RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "on", "off" });
    private static RuleDescriptor WrapRule(string key, string scope) => new(key, scope,
        RuleValue.FromChoice("inherit"), choices: new[] { "inherit", "none", "and", "or", "both" });
    private static RuleDescriptor[] SingleLineRules(string category)
    {
        var prefix = "subquery.singleLine." + category;
        return new[]
        {
            new RuleDescriptor(prefix + ".any", category, RuleValue.FromBoolean(false)),
            new RuleDescriptor(prefix + ".whenFitsMargin", category, RuleValue.FromBoolean(false)),
            new RuleDescriptor(prefix + ".maxWords", category,
                RuleValue.FromThreshold(new ThresholdRule(false, 10)), 0, 10000),
            new RuleDescriptor(prefix + ".maxCharacters", category,
                RuleValue.FromThreshold(new ThresholdRule(false, 50)), 0, 1000000)
        };
    }
    public IReadOnlyDictionary<string, RuleDescriptor> Definitions => definitions;
    public bool TryGet(string key, out RuleDescriptor? descriptor) => definitions.TryGetValue(key, out descriptor);
}

/// <summary>Immutable overrides layered over rule defaults.</summary>
public sealed class RuleOptions
{
    private readonly IReadOnlyDictionary<string, RuleValue> overrides;

    public RuleOptions(RuleCatalog catalog, IEnumerable<KeyValuePair<string, RuleValue>>? values = null)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        var map = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        foreach (var pair in values ?? Array.Empty<KeyValuePair<string, RuleValue>>())
        {
            if (!catalog.TryGet(pair.Key, out var descriptor) || descriptor is null ||
                !descriptor.Accepts(pair.Value) || map.ContainsKey(pair.Key))
                throw new ArgumentException($"Unknown, duplicate or invalid rule: {pair.Key}", nameof(values));
            map.Add(pair.Key, pair.Value);
        }
        overrides = new ReadOnlyDictionary<string, RuleValue>(map);
    }

    public RuleCatalog Catalog { get; }
    public IReadOnlyDictionary<string, RuleValue> Overrides => overrides;

    public RuleValue Get(string key) => overrides.TryGetValue(key, out var value) ? value
        : Catalog.TryGet(key, out var descriptor) && descriptor is not null ? descriptor.DefaultValue
        : throw new KeyNotFoundException($"Unknown formatting rule: {key}");

    public RuleOptions With(string key, RuleValue value)
    {
        var changed = overrides.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        changed[key] = value;
        return new RuleOptions(Catalog, changed);
    }
}
