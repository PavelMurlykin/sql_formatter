using System.Drawing;
using System.Windows.Forms;

namespace TSqlFormatter.IdeShared;

/// <summary>A resizable editor; closing it never commits a partially edited draft.</summary>
internal sealed class FormattingSettingsWindow : Form
{
    internal FormattingSettingsWindow(FullSettingsControl editor)
    {
        Text = SettingsAppearance.Text("SQL Formatter — настройки", "SQL Formatter — Settings");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1280, 860);
        MinimumSize = new Size(1000, 680);
        ShowInTaskbar = false;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = SettingsAppearance.Text("Закрыть", "Close"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = SettingsAppearance.Text("Сохранить и закрыть", "Save and close"), AutoSize = true };
        ok.Click += (_, _) => { if (editor.Save()) DialogResult = DialogResult.OK; };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        editor.Dock = DockStyle.Fill;
        Controls.Add(editor); Controls.Add(buttons);
        // Enter in the SQL editor inserts a newline, not acceptance of the dialog.
        CancelButton = cancel;
        FormClosing += (_, _) => editor.StopPreview();
    }
}
