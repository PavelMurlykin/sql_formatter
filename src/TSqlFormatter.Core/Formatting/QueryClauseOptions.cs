namespace TSqlFormatter.Core.Formatting;

public sealed class QueryClauseOptions
{
    public QueryClauseOptions(
        ClauseItemLayout groupByLayout = ClauseItemLayout.Auto,
        ClauseItemLayout orderByLayout = ClauseItemLayout.Auto)
    {
        if (!Enum.IsDefined(typeof(ClauseItemLayout), groupByLayout))
            throw new ArgumentOutOfRangeException(nameof(groupByLayout));
        if (!Enum.IsDefined(typeof(ClauseItemLayout), orderByLayout))
            throw new ArgumentOutOfRangeException(nameof(orderByLayout));

        GroupByLayout = groupByLayout;
        OrderByLayout = orderByLayout;
    }

    public ClauseItemLayout GroupByLayout { get; }

    public ClauseItemLayout OrderByLayout { get; }
}
