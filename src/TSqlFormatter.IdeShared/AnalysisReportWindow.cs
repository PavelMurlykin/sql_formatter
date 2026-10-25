using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TSqlFormatter.Core.Analysis;

namespace TSqlFormatter.IdeShared;

/// <summary>Modeless report of analysis findings; double-click or Enter navigates to a finding.</summary>
public sealed class AnalysisReportWindow : Form
{
    private readonly ListView findingsList;

    public AnalysisReportWindow(IReadOnlyList<AnalysisDiagnostic> diagnostics)
    {
        var title = SettingsAppearance.Text("Анализ SQL", "SQL Analysis");
        Text = diagnostics.Count == 0
            ? title + " — " + SettingsAppearance.Text("замечаний нет", "no findings")
            : title + " — " + string.Format(
                SettingsAppearance.Text("замечаний: {0}", "findings: {0}"), diagnostics.Count);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(760, 420);
        MinimumSize = new Size(440, 220);

        var close = new Button
        {
            Dock = DockStyle.Bottom,
            Text = SettingsAppearance.Text("Закрыть", "Close")
        };
        close.Click += (_, _) => Close();
        CancelButton = close;

        findingsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false
        };
        findingsList.Columns.Add(SettingsAppearance.Text("Позиция", "Position"), 90);
        findingsList.Columns.Add(SettingsAppearance.Text("Код", "Code"), 80);
        findingsList.Columns.Add(SettingsAppearance.Text("Серьёзность", "Severity"), 90);
        findingsList.Columns.Add(SettingsAppearance.Text("Сообщение", "Message"), 460);
        foreach (var diagnostic in diagnostics)
        {
            var item = new ListViewItem($"{diagnostic.Line},{diagnostic.Column}") { Tag = diagnostic };
            item.SubItems.Add(diagnostic.Code);
            item.SubItems.Add(diagnostic.Severity.ToString());
            item.SubItems.Add(diagnostic.Message);
            findingsList.Items.Add(item);
        }

        findingsList.DoubleClick += (_, _) => RaiseNavigate();
        findingsList.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                RaiseNavigate();
                args.Handled = true;
            }
        };

        Controls.Add(findingsList);
        Controls.Add(close);
    }

    public event Action<AnalysisDiagnostic>? Navigate;

    private void RaiseNavigate()
    {
        if (findingsList.SelectedItems.Count == 0 ||
            findingsList.SelectedItems[0].Tag is not AnalysisDiagnostic diagnostic)
        {
            return;
        }

        Navigate?.Invoke(diagnostic);
    }
}
