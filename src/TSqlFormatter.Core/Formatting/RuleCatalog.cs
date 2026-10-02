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

    public static RuleCatalog Default { get; } = CreateDefault();

    private static RuleCatalog CreateDefault()
    {
        var descriptors = new[]
    {
        CaseRule("textCase.keyword", "inherit"),
        CaseRule("textCase.builtin", "inherit"),
        CaseRule("textCase.dataType", "inherit"),
        CaseRule("textCase.identifier", "preserve"),
        CaseRule("textCase.variable", "preserve"),
        CaseRule("textCase.globalVariable", "inherit"),
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
            dependsOn: "subquery.list.stackColumns"),
        IndentDescriptor("case.caseIndent", "CASE keyword"),
        IndentDescriptor("case.codeIndent", "CASE THEN/ELSE result"),
        IndentDescriptor("case.inputIndent", "simple CASE input"),
        IndentDescriptor("case.thenKeywordIndent", "CASE THEN keyword"),
        IndentDescriptor("case.whenExpressionIndent", "CASE WHEN condition"),
        IndentDescriptor("case.whenKeywordIndent", "CASE WHEN/ELSE keyword"),
        IndentDescriptor("case.nestedConditionIndent", "CASE nested Boolean condition"),
        BreakRule("case.breakAfterThenElse", "CASE THEN/ELSE result"),
        BreakRule("case.breakBeforeCase", "CASE keyword"),
        BreakRule("case.breakBeforeEnd", "CASE END keyword"),
        BreakRule("case.breakBeforeInput", "simple CASE input"),
        BreakRule("case.breakBeforeThen", "CASE THEN keyword"),
        BreakRule("case.breakBeforeWhenElse", "CASE WHEN/ELSE keyword"),
        WrapRule("case.wrapCondition", "searched CASE condition"),
        BreakRule("case.wrapBeforeOperator", "searched CASE AND/OR operator"),
        BreakRule("case.wrapAfterOperator", "searched CASE AND/OR operand"),
        IndentDescriptor("setOperator.keywordIndent", "UNION/EXCEPT/INTERSECT keyword"),
        IndentDescriptor("setOperator.branchIndent", "UNION/EXCEPT/INTERSECT right branch"),
        BreakRule("setOperator.breakBefore", "UNION/EXCEPT/INTERSECT keyword"),
        BreakRule("setOperator.breakAfter", "UNION/EXCEPT/INTERSECT right branch")
        }.Concat(new[] { "allAnySomeExists", "cteQueries", "fromList", "inOperator", "other" }
            .SelectMany(SingleLineRules)).Concat(InsertRules()).ToArray();
        var scopes = new[] { "from.", "join.", "where.", "groupBy.", "having.",
            "orderBy.", "cte.", "for." };
        var subquery = descriptors.Where(descriptor => scopes.Any(scope =>
                descriptor.Key.StartsWith("select." + scope, StringComparison.Ordinal)))
            .Select(descriptor => new RuleDescriptor(
                "subquery." + descriptor.Key.Substring("select.".Length),
                "subquery " + descriptor.Scope, descriptor.DefaultValue,
                descriptor.Minimum, descriptor.Maximum, descriptor.Choices,
                descriptor.DependsOn?.Replace("select.", "subquery."), descriptor.Dialect));
        return new RuleCatalog(descriptors.Concat(subquery).Concat(UpdateDeleteRules(descriptors))
            .Concat(MergeHeaderRules(descriptors)).Concat(MergeBranchRules(descriptors)).Concat(MergeTailRules())
            .Concat(DeclareRules()).Concat(CodeRules()).Concat(ModuleRules()).Concat(CreateTableRules()).Concat(TriggerRules())
            .Concat(ExecuteLabelRules()).Concat(ProfileLayoutRules()));
    }

    private static IEnumerable<RuleDescriptor> ProfileLayoutRules()
    {
        const string scope = "profile layout";
        foreach (var key in new[] { "dmlCompact", "ddlCompact", "parenthesesCompact", "caseCompact", "subqueryCompact" })
            yield return new RuleDescriptor("layout." + key, scope,
                RuleValue.FromThreshold(new ThresholdRule(false, 100)), 0, 1000000);
        foreach (var key in new[] { "listFirstItem", "functionArguments", "inValues" })
            yield return new RuleDescriptor("layout." + key, scope, RuleValue.FromChoice("inherit"),
                choices: new[] { "inherit", "always", "never", "multiple", "ifLong" });
        yield return IndentDescriptor("layout.listIndent", scope);
        yield return new RuleDescriptor("layout.parenthesesStyle", scope, RuleValue.FromChoice("inherit"),
            choices: new[] { "inherit", "expandedToStatement", "compact" });
        foreach (var key in new[] { "blankLinesBetweenStatements", "blankLinesAfterBatch" })
            yield return new RuleDescriptor("layout." + key, scope, RuleValue.FromInteger(-1), -1, 10);
        foreach (var key in new[] { "alignDeclarationValues", "alignDdlTypes", "alignListComments", "alignCommentGroups",
                     "setValueOnNewLineIfLong", "newLineAfterTop", "restoreMoveOnNewLine", "restoreToOnNewLine", "respectFormattingDirectives" })
            yield return new RuleDescriptor("layout." + key, scope, RuleValue.FromBoolean(false));
        yield return SpacingRule("spacing.comparisonOperators");
        yield return SpacingRule("spacing.beforeTypeParameters");
        yield return SpacingRule("spacing.beforeSemicolon");
    }

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
    private static IEnumerable<RuleDescriptor> InsertRules()
    {
        const string scope = "INSERT";
        foreach (var key in new[] { "into.keywordIndent", "into.tableIndent", "columns.listIndent",
                     "columns.braceIndent", "output.keywordIndent", "output.listIndent", "source.indent",
                     "values.listIndent", "values.braceIndent", "values.keywordIndent" })
            yield return IndentDescriptor("insert." + key, scope);
        foreach (var key in new[] { "into.breakBefore", "into.breakBeforeTable", "columns.breakAfterOpen",
                     "columns.breakBeforeClose", "columns.breakBeforeOpen", "output.breakAfter",
                     "output.breakBefore", "source.breakBefore", "values.breakAfterOpen",
                     "values.breakAfterKeyword", "values.breakBeforeClose", "values.breakBeforeKeyword" })
            yield return BreakRule("insert." + key, scope);
        foreach (var key in new[] { "columns.spaceBeforeOpen", "columns.spaceWithin",
                     "values.spaceAfterKeyword", "values.spaceWithin" })
            yield return SpacingRule("insert." + key);
        foreach (var group in new[] { "columns", "output", "values" })
        {
            yield return StackRule("insert." + group + ".stackList", scope);
            yield return new RuleDescriptor("insert." + group + ".stackMode", scope,
                RuleValue.FromChoice("onePerLine"), choices: new[] { "onePerLine", "auto" },
                dependsOn: "insert." + group + ".stackList");
        }
        yield return StackRule("insert.values.stackRows", scope);
        yield return new RuleDescriptor("insert.values.stackRowsMode", scope,
            RuleValue.FromChoice("onePerLine"), choices: new[] { "onePerLine", "auto" },
            dependsOn: "insert.values.stackRows");
        yield return new RuleDescriptor("insert.source.singleLine.any", scope, RuleValue.FromBoolean(false));
        yield return new RuleDescriptor("insert.source.singleLine.whenFitsMargin", scope, RuleValue.FromBoolean(false));
        yield return new RuleDescriptor("insert.source.singleLine.maxWords", scope,
            RuleValue.FromThreshold(new ThresholdRule(false, 10)), 0, 10000);
        yield return new RuleDescriptor("insert.source.singleLine.maxCharacters", scope,
            RuleValue.FromThreshold(new ThresholdRule(false, 50)), 0, 1000000);
    }
    private static IEnumerable<RuleDescriptor> UpdateDeleteRules(IEnumerable<RuleDescriptor> selectRules)
    {
        foreach (var prefix in new[] { "update", "delete" })
        {
            foreach (var descriptor in selectRules.Where(rule => new[] { "from.", "join.", "where.", "option." }
                         .Any(group => rule.Key.StartsWith("select." + group, StringComparison.Ordinal))))
                yield return new RuleDescriptor(prefix + descriptor.Key.Substring("select".Length),
                    prefix.ToUpperInvariant() + " " + descriptor.Scope, descriptor.DefaultValue,
                    descriptor.Minimum, descriptor.Maximum, descriptor.Choices,
                    descriptor.DependsOn?.Replace("select.", prefix + "."), descriptor.Dialect);
            yield return new RuleDescriptor(prefix + ".from.useSelectFormatting", prefix + " FROM/JOIN",
                RuleValue.FromBoolean(false));
            foreach (var key in new[] { "output.keywordIndent", "output.listIndent", "target.indent" })
                yield return IndentDescriptor(prefix + "." + key, prefix);
            foreach (var key in new[] { "output.breakBefore", "output.breakAfter", "target.breakBefore" })
                yield return BreakRule(prefix + "." + key, prefix);
            foreach (var descriptor in ListRules(prefix + ".output", prefix)) yield return descriptor;
        }
        foreach (var key in new[] { "keywordIndent", "listIndent" })
            yield return IndentDescriptor("update.set." + key, "UPDATE SET");
        foreach (var key in new[] { "breakBefore", "breakAfter" })
            yield return BreakRule("update.set." + key, "UPDATE SET");
        foreach (var descriptor in ListRules("update.set", "UPDATE SET")) yield return descriptor;
        yield return IndentDescriptor("delete.target.fromKeywordIndent", "DELETE header FROM");
        yield return BreakRule("delete.target.breakBeforeFrom", "DELETE header FROM");
    }

    private static IEnumerable<RuleDescriptor> ListRules(string prefix, string scope)
    {
        yield return StackRule(prefix + ".stackList", scope);
        yield return new RuleDescriptor(prefix + ".stackMode", scope, RuleValue.FromChoice("onePerLine"),
            choices: new[] { "onePerLine", "auto" }, dependsOn: prefix + ".stackList");
    }

    private static IEnumerable<RuleDescriptor> MergeHeaderRules(IEnumerable<RuleDescriptor> selectRules)
    {
        const string scope = "MERGE header/source";
        foreach (var descriptor in selectRules.Where(rule => rule.Key.StartsWith("select.join.", StringComparison.Ordinal)))
            yield return new RuleDescriptor("merge.join." + descriptor.Key.Substring("select.join.".Length),
                scope + " " + descriptor.Scope, descriptor.DefaultValue, descriptor.Minimum,
                descriptor.Maximum, descriptor.Choices);
        yield return new RuleDescriptor("merge.join.useSelectFormatting", scope, RuleValue.FromBoolean(false));
        foreach (var key in new[] { "into.keywordIndent", "into.tableIndent", "hints.keywordIndent", "hints.listIndent",
                     "hints.braceIndent", "using.keywordIndent", "on.keywordIndent", "on.conditionIndent",
                     "on.nestedConditionIndent", "values.keywordIndent", "values.listIndent", "values.braceIndent" })
            yield return IndentDescriptor("merge." + key, scope);
        foreach (var key in new[] { "into.breakBefore", "into.breakBeforeTable", "hints.breakBefore", "hints.breakBeforeOpen",
                     "hints.breakAfterOpen", "hints.breakBeforeClose", "using.breakBefore", "using.breakAfter",
                     "on.breakBefore", "on.breakAfter", "on.wrapBeforeOperator", "on.wrapAfterOperator",
                     "values.breakBeforeKeyword", "values.breakAfterKeyword", "values.breakAfterOpen", "values.breakBeforeClose" })
            yield return BreakRule("merge." + key, scope);
        yield return WrapRule("merge.on.wrapCondition", scope);
        foreach (var key in new[] { "hints.spaceBeforeOpen", "hints.spaceWithin", "values.spaceAfterKeyword", "values.spaceWithin" })
            yield return SpacingRule("merge." + key);
        foreach (var descriptor in ListRules("merge.values", scope)) yield return descriptor;
        yield return StackRule("merge.values.stackRows", scope);
        yield return new RuleDescriptor("merge.values.stackRowsMode", scope, RuleValue.FromChoice("onePerLine"),
            choices: new[] { "onePerLine", "auto" }, dependsOn: "merge.values.stackRows");
    }

    private static IEnumerable<RuleDescriptor> MergeBranchRules(IEnumerable<RuleDescriptor> rules)
    {
        const string scope = "MERGE branches";
        foreach (var key in new[] { "when.keywordIndent", "when.conditionIndent", "when.nestedConditionIndent",
                     "then.keywordIndent", "then.actionIndent" })
            yield return IndentDescriptor("merge." + key, scope);
        foreach (var key in new[] { "when.breakBefore", "when.breakAfter", "when.wrapBeforeOperator",
                     "when.wrapAfterOperator", "then.breakBefore", "then.breakAfter" })
            yield return BreakRule("merge." + key, scope);
        yield return WrapRule("merge.when.wrapCondition", scope);
        var actions = rules.Where(rule => rule.Key.StartsWith("insert.columns.", StringComparison.Ordinal)
            || rule.Key.StartsWith("insert.values.", StringComparison.Ordinal))
            .Where(rule => !rule.Key.Contains("stackRows"))
            .Concat(UpdateDeleteRules(rules).Where(rule => rule.Key.StartsWith("update.set.", StringComparison.Ordinal)));
        foreach (var descriptor in actions)
            yield return new RuleDescriptor("merge." + descriptor.Key, scope, descriptor.DefaultValue,
                descriptor.Minimum, descriptor.Maximum, descriptor.Choices,
                descriptor.DependsOn is null ? null : "merge." + descriptor.DependsOn, descriptor.Dialect);
        foreach (var action in new[] { "update", "insert" })
            yield return new RuleDescriptor("merge." + action + ".useStatementFormatting", scope, RuleValue.FromBoolean(false));
    }

    private static IEnumerable<RuleDescriptor> MergeTailRules()
    {
        const string scope = "MERGE TOP/OUTPUT/OPTION";
        foreach (var key in new[] { "top.keywordIndent", "top.percentIndent", "output.keywordIndent",
                     "output.listIndent", "option.keywordIndent", "option.hintsIndent" })
            yield return IndentDescriptor("merge." + key, scope);
        foreach (var key in new[] { "top.breakBefore", "top.breakBeforeOpen", "top.breakAfterOpen",
                     "top.breakBeforeClose", "top.breakBeforePercent", "output.breakBefore", "output.breakAfter",
                     "option.breakBefore", "option.breakAfter" })
            yield return BreakRule("merge." + key, scope);
        foreach (var key in new[] { "top.spaceAfterKeyword", "top.spaceWithin" }) yield return SpacingRule("merge." + key);
        foreach (var rule in ListRules("merge.output", scope)) yield return rule;
    }

    private static IEnumerable<RuleDescriptor> DeclareRules()
    {
        const string scope = "DECLARE";
        foreach (var key in new[] { "variables.listIndent", "variables.tableIndent", "cursor.keywordIndent",
                     "cursor.forIndent", "cursor.queryIndent" }) yield return IndentDescriptor("declare." + key, scope);
        foreach (var key in new[] { "variables.breakAfter", "variables.breakBeforeTable", "cursor.breakBefore",
                     "cursor.breakBeforeFor", "cursor.breakBeforeQuery" }) yield return BreakRule("declare." + key, scope);
        foreach (var rule in ListRules("declare.variables", scope)) yield return rule;
        foreach (var rule in SingleLineRules("cursor"))
            yield return new RuleDescriptor(rule.Key.Replace("subquery.singleLine.cursor", "declare.cursor.singleLine"),
                scope, rule.DefaultValue, rule.Minimum, rule.Maximum, rule.Choices);
    }

    private static IEnumerable<RuleDescriptor> CodeRules()
    {
        const string scope = "control flow";
        foreach (var key in new[] { "breakAfterBegin", "breakBeforeEnd", "separateStatements", "block.breakBeforeCatch" })
            yield return BreakRule("code." + key, scope);
        yield return IndentDescriptor("code.transaction.bodyIndent", scope);
        foreach (var prefix in new[] { "block", "if", "while" })
        {
            yield return new RuleDescriptor("code." + prefix + ".blankLinesAround", scope, RuleValue.FromBoolean(false));
            yield return IndentDescriptor("code." + prefix + ".bodyIndent", scope);
            if (prefix == "block") continue;
            foreach (var key in new[] { "keywordIndent", "conditionIndent", "nestedConditionIndent" })
                yield return IndentDescriptor("code." + prefix + "." + key, scope);
            foreach (var key in new[] { "breakAfterCondition", "wrapBeforeOperator", "wrapAfterOperator" })
                yield return BreakRule("code." + prefix + "." + key, scope);
            yield return WrapRule("code." + prefix + ".wrapCondition", scope);
        }
        yield return BreakRule("code.if.breakBeforeElse", scope);
        yield return BreakRule("code.if.breakAfterElse", scope);
    }

    private static IEnumerable<RuleDescriptor> ModuleRules()
    {
        const string scope = "procedure/function/view";
        foreach (var key in new[] { "body.asIndent", "body.keywordIndent", "body.codeIndent", "parameters.listIndent",
                     "parameters.braceIndent", "returns.tableIndent", "with.keywordIndent", "with.listIndent" })
            yield return IndentDescriptor("routine." + key, scope);
        foreach (var key in new[] { "body.breakBeforeAs", "body.breakBefore", "parameters.breakBeforeOpen", "parameters.breakAfterOpen",
                     "parameters.breakBeforeClose", "returns.breakBefore", "returns.breakBeforeTable", "with.breakBefore", "with.breakAfter" })
            yield return BreakRule("routine." + key, scope);
        foreach (var key in new[] { "parameters.spaceBeforeOpen", "parameters.spaceWithin", "parameters.spaceWithinEmpty" })
            yield return SpacingRule("routine." + key);
        foreach (var prefix in new[] { "routine.parameters", "routine.with" })
            foreach (var rule in ListRules(prefix, scope)) yield return rule;
        foreach (var descriptor in InsertRules().Where(d => d.Key.StartsWith("insert.columns.", StringComparison.Ordinal)))
            yield return new RuleDescriptor(descriptor.Key.Replace("insert.columns.", "view.columns."), scope,
                descriptor.DefaultValue, descriptor.Minimum, descriptor.Maximum, descriptor.Choices,
                descriptor.DependsOn?.Replace("insert.columns.", "view.columns."));
        foreach (var key in new[] { "asIndent", "queryIndent" }) yield return IndentDescriptor("view.query." + key, scope);
        foreach (var key in new[] { "breakBeforeAs", "breakAfterAs" }) yield return BreakRule("view.query." + key, scope);
        foreach (var rule in SingleLineRules("view"))
            yield return new RuleDescriptor(rule.Key.Replace("subquery.singleLine.view", "view.query.singleLine"), scope,
                rule.DefaultValue, rule.Minimum, rule.Maximum, rule.Choices);
    }

    private static IEnumerable<RuleDescriptor> CreateTableRules()
    {
        const string scope = "CREATE TABLE";
        foreach (var descriptor in InsertRules().Where(d => d.Key.StartsWith("insert.columns.", StringComparison.Ordinal)))
            yield return new RuleDescriptor(descriptor.Key.Replace("insert.columns.", "createTable.columns."), scope,
                descriptor.DefaultValue, descriptor.Minimum, descriptor.Maximum, descriptor.Choices,
                descriptor.DependsOn?.Replace("insert.columns.", "createTable.columns."));
        yield return new RuleDescriptor("createTable.blankLinesAround", scope, RuleValue.FromBoolean(false));
        yield return IndentDescriptor("createTable.storage.listIndent", scope);
        yield return BreakRule("createTable.storage.breakBefore", scope);
        foreach (var rule in ListRules("createTable.storage", scope)) yield return rule;
    }

    private static IEnumerable<RuleDescriptor> TriggerRules()
    {
        const string scope = "trigger";
        foreach (var key in new[] { "on.keywordIndent", "on.targetIndent", "events.keywordIndent", "events.listIndent",
                     "with.keywordIndent", "with.listIndent", "body.asIndent", "body.keywordIndent", "body.codeIndent" })
            yield return IndentDescriptor("trigger." + key, scope);
        foreach (var key in new[] { "on.breakBefore", "on.breakAfter", "events.breakBefore", "events.breakAfter",
                     "with.breakBefore", "with.breakAfter", "body.breakBeforeAs", "body.breakAfterAs" })
            yield return BreakRule("trigger." + key, scope);
        foreach (var prefix in new[] { "trigger.events", "trigger.with" })
            foreach (var rule in ListRules(prefix, scope)) yield return rule;
    }

    private static IEnumerable<RuleDescriptor> ExecuteLabelRules()
    {
        yield return IndentDescriptor("execute.parameters.listIndent", "EXECUTE");
        yield return BreakRule("execute.parameters.breakBefore", "EXECUTE");
        foreach (var rule in ListRules("execute.parameters", "EXECUTE")) yield return rule;
        yield return IndentDescriptor("labels.indent", "label");
        yield return BreakRule("labels.breakAfter", "label");
        yield return new RuleDescriptor("labels.blankLinesAround", "labeled statement/block", RuleValue.FromBoolean(false));
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
