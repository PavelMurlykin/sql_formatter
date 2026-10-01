using System.Drawing;
using System.Windows.Forms;

namespace TSqlFormatter.IdeShared;

/// <summary>A resizable editor; closing it never commits a partially edited draft.</summary>
internal sealed class FormattingSettingsWindow : Form
{
    internal FormattingSettingsWindow(FullSettingsControl editor)
    {
        Text = "SQL Formatter — настройки и профили";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1120, 820);
        MinimumSize = new Size(820, 620);
        ShowInTaskbar = false;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        editor.Dock = DockStyle.Fill;
        Controls.Add(editor); Controls.Add(buttons);
        // Enter in the SQL editor inserts a newline, not acceptance of the dialog.
        CancelButton = cancel;
        FormClosing += (_, _) => editor.StopPreview();
    }
}
