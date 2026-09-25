using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Configuration;

/// <summary>Explicit option values (for example, from CLI flags) layered over a baseline.</summary>
public sealed class FormattingOptionsOverrides
{
    public FormattingOptionsOverrides(
        int? maxLineLength = null,
        DocLineEnding? lineEnding = null,
        bool? finalNewLine = null,
        int? indentSize = null,
        bool? useTabs = null,
        KeywordCase? keywordCase = null,
        SelectColumnLayout? selectColumns = null,
        ClauseItemLayout? groupByLayout = null,
        ClauseItemLayout? orderByLayout = null)
    {
        MaxLineLength = maxLineLength;
        LineEnding = lineEnding;
        FinalNewLine = finalNewLine;
        IndentSize = indentSize;
        UseTabs = useTabs;
        KeywordCase = keywordCase;
        SelectColumns = selectColumns;
        GroupByLayout = groupByLayout;
        OrderByLayout = orderByLayout;
    }

    public int? MaxLineLength { get; }
    public DocLineEnding? LineEnding { get; }
    public bool? FinalNewLine { get; }
    public int? IndentSize { get; }
    public bool? UseTabs { get; }
    public KeywordCase? KeywordCase { get; }
    public SelectColumnLayout? SelectColumns { get; }
    public ClauseItemLayout? GroupByLayout { get; }
    public ClauseItemLayout? OrderByLayout { get; }

    public FormattingOptions ApplyTo(FormattingOptions baseline)
    {
        if (baseline is null) throw new ArgumentNullException(nameof(baseline));

        return new FormattingOptions(
            new GeneralOptions(
                MaxLineLength ?? baseline.General.MaxLineWidth,
                LineEnding ?? baseline.General.LineEnding,
                FinalNewLine ?? baseline.General.FinalNewline),
            new IndentOptions(
                IndentSize ?? baseline.Indent.Size,
                UseTabs ?? baseline.Indent.UseTabs),
            new KeywordOptions(KeywordCase ?? baseline.Keywords.Case),
            new SelectOptions(SelectColumns ?? baseline.Select.ColumnLayout),
            new QueryClauseOptions(
                GroupByLayout ?? baseline.Clauses.GroupByLayout,
                OrderByLayout ?? baseline.Clauses.OrderByLayout));
    }
}
