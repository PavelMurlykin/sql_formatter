using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using TSqlFormatter.Core.QueryResults;

namespace TSqlFormatter.IdeShared;

internal enum ClipboardResultKind { In, Insert, Statistics }

internal sealed class QueryResultsOptionsWindow : Form
{
    private readonly ClipboardResultKind kind;
    private readonly CheckBox headers = new() { AutoSize = true, AccessibleName = "TsvHeaders" };
    private readonly CheckBox text = new() { AutoSize = true, AccessibleName = "TsvAllText" };
    private readonly TextBox table = new() { Width = 700, MaxLength = 512, AccessibleName = "TsvTargetTable" };
    private readonly Label error = new() { AutoSize = true, MaximumSize = new Size(740, 0), AccessibleName = "TsvOptionsError" };
    internal QueryResultsOptionsWindow(ClipboardResultKind kind, int rows, int columns)
    {
        this.kind = kind; Text = Title(kind);
        table.TextChanged += (_, _) => error.Text = "";
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        Size = new Size(840, 420); MinimumSize = new Size(620, 360); MinimizeBox = MaximizeBox = false;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        flow.Controls.Add(new Label { AutoSize = true, Text = string.Format(T("Буфер TSV: строк {0}, столбцов {1}.",
            "Clipboard TSV: {0} rows, {1} columns."), rows, columns) });
        headers.Text = T("Первая строка — заголовки (по умолчанию все строки — данные)", "First row contains headers (all rows are data by default)");
        flow.Controls.Add(headers);
        text.Text = T("Все значения как текст, включая NULL", "All values as text, including NULL");
        text.Visible = kind != ClipboardResultKind.Statistics; flow.Controls.Add(text);
        if (kind == ClipboardResultKind.Insert)
        {
            flow.Controls.Add(new Label { AutoSize = true, Text = T("Целевая таблица: имя, схема.имя или база.схема.имя; допустимы [скобки].",
                "Target table: name, schema.name or database.schema.name; [brackets] are supported.") });
            flow.Controls.Add(table);
        }
        flow.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(740, 0), Margin = new Padding(3, 12, 3, 12), Text = kind == ClipboardResultKind.Statistics ?
            T("Считаются числовые ячейки TSV из буфера, не выделенный SQL. Дробный разделитель — точка; пробелы по краям чисел игнорируются. Нечисловые ячейки пропускаются. При потере точности результат отклоняется.",
                "Counts numeric TSV cells from the clipboard, not selected SQL. Use a decimal point; surrounding numeric whitespace is ignored. Nonnumeric cells are skipped. Inexact sums or values are rejected.") :
            T("IN требует один столбец. В авто-режиме тип выбирается по столбцу; смешанные данные и ведущие нули сохраняются как текст. Пустая ячейка — пустая строка, NULL без кавычек — SQL NULL. Даты — ISO. SQL появится в предпросмотре и не выполняется.",
                "IN requires one column. Auto mode infers each column; mixed data and leading zeros stay text. Empty cells are empty strings; unquoted NULL is SQL NULL. Dates use ISO formats. SQL is previewed and never executed.") });
        flow.Controls.Add(error);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { AutoSize = true, Text = T("Отмена", "Cancel"), DialogResult = DialogResult.Cancel };
        var next = new Button { AutoSize = true, Text = T("Продолжить", "Continue"), AccessibleName = "TsvContinue" };
        next.Click += (_, _) =>
        {
            try
            {
                if (this.kind == ClipboardResultKind.Insert) QueryResultSql.NormalizeTableName(table.Text);
                error.Text = ""; DialogResult = DialogResult.OK;
            }
            catch (ResultDataException ex) { error.Text = QueryResultsText.Error(ex); }
        };
        buttons.Controls.Add(cancel); buttons.Controls.Add(next); Controls.Add(flow); Controls.Add(buttons);
        AcceptButton = next; CancelButton = cancel;
    }
    internal QueryResultOptions Options => new(headers.Checked, text.Checked);
    internal string TableName => table.Text;
    internal void ApplyAppearance(SettingsAppearance appearance)
    { appearance.Apply(this); error.ForeColor = appearance.Dark ? Color.LightCoral : Color.Firebrick; }
    internal static string Title(ClipboardResultKind kind) => kind switch
    {
        ClipboardResultKind.In => T("Вставить как IN (…) — буфер обмена", "Paste as IN (…) — Clipboard"),
        ClipboardResultKind.Insert => T("Сформировать INSERT — буфер обмена", "Generate INSERT — Clipboard"),
        _ => T("Статистика выделенного — буфер обмена", "Selection Statistics — Clipboard")
    };
    private static string T(string ru, string en) => SettingsAppearance.Text(ru, en);
}

internal sealed class QueryResultsStatisticsWindow : Form
{
    internal QueryResultsStatisticsWindow(TsvStatistics statistics)
    {
        Text = QueryResultsOptionsWindow.Title(ClipboardResultKind.Statistics);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        Size = new Size(720, 310); MinimumSize = new Size(540, 260); MinimizeBox = MaximizeBox = false;
        string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
        var result = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both,
            WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 11), AccessibleName = "TsvStatistics",
            Text = string.Format(SettingsAppearance.Text(
                "Всего ячеек: {0}\r\nЧисловых: {1}\r\nПропущено: {2}\r\nСумма: {3}\r\nСреднее: {4}",
                "Total cells: {0}\r\nNumeric: {1}\r\nSkipped: {2}\r\nSum: {3}\r\nAverage: {4}"),
                statistics.TotalCells, statistics.NumericCount, statistics.SkippedCount, Number(statistics.Sum),
                statistics.Average is { } average ? Number(average) : SettingsAppearance.Text("не определено", "undefined")) };
        var note = new Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), Text = SettingsAppearance.Text(
            "Среднее округляется до точности decimal (28–29 значащих цифр). Буфер обмена и SQL не изменены.",
            "Average is rounded to decimal precision (28–29 significant digits). Clipboard and SQL are unchanged.") };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { AutoSize = true, Text = SettingsAppearance.Text("Закрыть", "Close"), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(close); Controls.Add(result); Controls.Add(note); Controls.Add(buttons); CancelButton = close;
    }
}

internal static class QueryResultsText
{
    internal static string Error(ResultDataException ex) => !SettingsAppearance.Russian ? ex.Message : ex.Code switch
    {
        ResultDataError.EmptyInput => "В буфере обмена нет текста TSV.",
        ResultDataError.TooLarge => "TSV превышает 1 Ми символов.",
        ResultDataError.TooManyRows => "TSV превышает 10000 строк, включая заголовки.",
        ResultDataError.TooManyColumns => "TSV превышает 256 столбцов.",
        ResultDataError.TooManyCells => "TSV превышает 100000 ячеек.",
        ResultDataError.MalformedTsv => "Неверные кавычки TSV или символ NUL.",
        ResultDataError.UnequalColumns => "Во всех строках TSV должно быть одинаковое число столбцов.",
        ResultDataError.NoDataRows => "После заголовка не осталось строк данных.",
        ResultDataError.InRequiresOneColumn => "Для IN нужен ровно один столбец TSV.",
        ResultDataError.InvalidHeaders => "Заголовки INSERT должны быть непустыми, уникальными и не длиннее 128 символов.",
        ResultDataError.InvalidTable => "Укажите от одной до трёх частей имени таблицы; допустимы [скобки].",
        ResultDataError.NumericOverflow => "Числовая ячейка или сумма не представима как точное decimal-число.",
        _ => "Сформированный SQL не удалось разобрать или отформатировать."
    };
}
