using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TSqlFormatter.IdeShared;

/// <summary>Lists the planned rename changes; Apply returns DialogResult.OK.</summary>
public sealed class RenamePreviewWindow : Form
{
    public RenamePreviewWindow(IReadOnlyList<(string Position, string OldText, string NewText)> changes)
    {
        Text = SettingsAppearance.Text("Предпросмотр переименования — изменений: ",
                "Rename preview — changes: ") + changes.Count;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(560, 340);
        MinimumSize = new Size(400, 220);

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var cancel = new Button
        {
            Text = SettingsAppearance.Text("Отмена", "Cancel"),
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(450, 8)
        };
        var apply = new Button
        {
            Text = SettingsAppearance.Text("Применить", "Apply"),
            DialogResult = DialogResult.OK,
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(350, 8)
        };
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false
        };
        list.Columns.Add(SettingsAppearance.Text("Позиция", "Position"), 90);
        list.Columns.Add(SettingsAppearance.Text("Было", "Old"), 160);
        list.Columns.Add(SettingsAppearance.Text("Станет", "New"), 260);
        foreach (var change in changes)
        {
            var item = new ListViewItem(change.Position);
            item.SubItems.Add(change.OldText);
            item.SubItems.Add(change.NewText);
            list.Items.Add(item);
        }

        Controls.Add(list);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = apply;
    }
}
