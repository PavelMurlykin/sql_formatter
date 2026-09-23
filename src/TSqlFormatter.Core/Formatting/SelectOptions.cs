namespace TSqlFormatter.Core.Formatting;

public sealed class SelectOptions
{
    public SelectOptions(SelectColumnLayout columnLayout = SelectColumnLayout.Auto)
    {
        if (!Enum.IsDefined(typeof(SelectColumnLayout), columnLayout))
        {
            throw new ArgumentOutOfRangeException(nameof(columnLayout));
        }

        ColumnLayout = columnLayout;
    }

    public SelectColumnLayout ColumnLayout { get; }
}
