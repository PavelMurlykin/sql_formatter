using System;
using System.Drawing;
using System.Windows.Forms;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Analysis;

namespace TSqlFormatter.IdeShared;

/// <summary>Read-only, explicit warning. It neither runs SQL nor intercepts execution.</summary>
internal sealed class DangerousCommandsWindow : Form
{
    private readonly Label environment;
    private readonly Label production;
    private readonly SqlSafetyCheck check;
    internal DangerousCommandsWindow(SqlSafetyCheck check)
    {
        this.check = check;
        Text = T("Проверка опасных команд", "Check Dangerous Commands") + " — " + check.Environment.Label;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        Size = new Size(920, 590); MinimumSize = new Size(650, 430);
        MinimizeBox = false; MaximizeBox = false;
        environment = new Label { Dock = DockStyle.Top, Height = 100, Padding = new Padding(12, 8, 12, 8),
            Font = new Font("Segoe UI", 10, FontStyle.Bold), AccessibleName = "EnvironmentBanner",
            Text = string.Format(T("Окружение: {0}\nСервер: {1}   База: {2}\nДокумент: {3}",
                "Environment: {0}\nServer: {1}   Database: {2}\nDocument: {3}"),
                check.Environment.Kind == SqlEnvironmentKind.Unknown ? T("НЕ ОПРЕДЕЛЕНО", "UNKNOWN") : check.Environment.Label,
                ContextName(check.Environment.Context.Server), ContextName(check.Environment.Context.Database), check.Environment.Context.DocumentMoniker) };
        production = new Label { Dock = DockStyle.Top, Height = 48, Padding = new Padding(10, 4, 10, 4),
            Font = new Font("Segoe UI", 12, FontStyle.Bold), AccessibleName = "ProductionWarning",
            Visible = check.Environment.Kind == SqlEnvironmentKind.Prod,
            Text = T("PROD — проверьте последствия изменений перед выполнением SQL.",
                "PROD — review the impact of changes before executing SQL.") };
        var status = new Label { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10, 8, 10, 4), AccessibleName = "SafetyStatus",
            Text = check.Error != null ? T("Проверка не завершена: ошибка разбора SQL. ", "Check incomplete: SQL parse error. ") + check.Error :
                check.Diagnostics.Count == 0 ? T("Поддерживаемых опасных команд не найдено. Окружение показано по текущему контексту редактора.",
                    "No supported dangerous statements found. Environment reflects the current editor context.") :
                string.Format(T("Потенциально опасных команд: {0}. Выберите строку для перехода.",
                    "Potentially dangerous statements: {0}. Select a row to navigate."), check.Diagnostics.Count) };
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            HideSelection = false, MultiSelect = false, AccessibleName = "DangerousFindings" };
        list.Columns.Add(T("Позиция", "Position"), 90); list.Columns.Add(T("Код", "Code"), 90);
        list.Columns.Add(T("Команда", "Statement"), 110); list.Columns.Add(T("Замечание", "Finding"), 560);
        foreach (var finding in check.Diagnostics)
        {
            var item = new ListViewItem($"{finding.Line},{finding.Column}") { Tag = finding };
            item.SubItems.Add(finding.Code); item.SubItems.Add(Operation(finding.Code)); item.SubItems.Add(Message(finding)); list.Items.Add(item);
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { AutoSize = true, Text = T("Закрыть", "Close"), DialogResult = DialogResult.Cancel };
        var navigate = new Button { AutoSize = true, Text = T("Перейти", "Go to"), AccessibleName = "NavigateDangerousFinding" };
        void SelectFinding()
        {
            if (list.SelectedItems.Count == 0) return;
            SelectedDiagnostic = (AnalysisDiagnostic)list.SelectedItems[0].Tag; DialogResult = DialogResult.OK;
        }
        navigate.Click += (_, _) => SelectFinding(); list.DoubleClick += (_, _) => SelectFinding();
        list.KeyDown += (_, args) => { if (args.KeyCode == Keys.Enter) { SelectFinding(); args.Handled = true; } };
        buttons.Controls.Add(close); buttons.Controls.Add(navigate); CancelButton = close;
        var scope = new Label { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(10, 4, 10, 4), Text = T(
            "Проверяется снимок всего документа, включая тела модулей. Динамический SQL не разбирается; USE и имена объектов не меняют метку контекста. Выполнение SQL не перехватывается.",
            "Checks a whole-document snapshot, including module bodies. Dynamic SQL is not parsed; USE and object names do not change the context label. SQL execution is not intercepted.") };
        Controls.Add(list); Controls.Add(status); Controls.Add(production); Controls.Add(environment);
        Controls.Add(scope); Controls.Add(buttons);
    }
    internal AnalysisDiagnostic? SelectedDiagnostic { get; private set; }
    internal void ShowForOwner(IntPtr owner, SettingsAppearance theme)
    {
        ApplyAppearance(theme);
        if (owner == IntPtr.Zero) ShowDialog(); else ShowDialog(new DialogOwner(owner));
    }
    private sealed class DialogOwner : IWin32Window
    {
        internal DialogOwner(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }
    internal void ApplyAppearance(SettingsAppearance theme)
    {
        theme.Apply(this);
        var color = ColorTranslator.FromHtml(check.Environment.Color);
        environment.BackColor = color; environment.ForeColor = color.GetBrightness() < 0.5f ? Color.White : Color.Black;
        production.ForeColor = theme.Dark ? Color.LightCoral : Color.Firebrick;
    }
    private static string ContextName(string? value) => string.IsNullOrWhiteSpace(value) ? T("недоступно", "unavailable") : value!;
    private static string Operation(string code) => code switch { "TSQL004" => "UPDATE", "TSQL005" => "DELETE", "TSQL006" => "DROP", _ => "TRUNCATE" };
    private static string Message(AnalysisDiagnostic finding) => !SettingsAppearance.Russian ? finding.Message : finding.Code switch
    {
        "TSQL004" => "UPDATE без WHERE.", "TSQL005" => "DELETE без WHERE.",
        "TSQL006" => "DROP удаляет объект базы данных.", "TSQL007" => "TRUNCATE TABLE удаляет строки без WHERE.", _ => finding.Message
    };
    private static string T(string ru, string en) => SettingsAppearance.Text(ru, en);
}
