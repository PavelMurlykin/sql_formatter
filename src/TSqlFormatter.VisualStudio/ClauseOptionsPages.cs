using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.VisualStudio;

public sealed class SelectOptionsPage : DialogPage
{
    [Category("SELECT"), DisplayName("Columns")]
    [Description("Auto or one SELECT column per line.")]
    [DefaultValue(SelectColumnLayout.Auto)]
    public SelectColumnLayout Columns { get; set; } = SelectColumnLayout.Auto;

    [Category("GROUP BY"), DisplayName("Items")]
    [Description("Auto or one GROUP BY item per line.")]
    [DefaultValue(ClauseItemLayout.Auto)]
    public ClauseItemLayout GroupByItems { get; set; } = ClauseItemLayout.Auto;

    [Category("ORDER BY"), DisplayName("Items")]
    [Description("Auto or one ORDER BY item per line.")]
    [DefaultValue(ClauseItemLayout.Auto)]
    public ClauseItemLayout OrderByItems { get; set; } = ClauseItemLayout.Auto;
}

public sealed class JoinOptionsPage : DialogPage
{
    [Category("JOIN"), DisplayName("JOIN on new line")]
    [Description("Place supported JOIN and APPLY clauses on a new line.")]
    [DefaultValue(true)]
    public bool ClauseNewLine { get; set; } = true;

    [Category("JOIN"), DisplayName("ON on new line")]
    [Description("Place supported ON conditions on a new, indented line.")]
    [DefaultValue(true)]
    public bool ConditionNewLine { get; set; } = true;
}

public sealed class WhereOptionsPage : DialogPage
{
    [Category("WHERE / HAVING"), DisplayName("Condition on new line")]
    [Description("Place a supported condition after WHERE or HAVING on a new, indented line.")]
    [DefaultValue(true)]
    public bool ConditionNewLine { get; set; } = true;

    [Category("WHERE / HAVING"), DisplayName("AND/OR on new line")]
    [Description("Place supported AND/OR operators on a new line.")]
    [DefaultValue(true)]
    public bool BooleanOperatorNewLine { get; set; } = true;
}
