using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TSqlFormatter.Core.History;

namespace TSqlFormatter.IdeShared;

/// <summary>
/// Modal picker over local editor snapshots: filter by name/path/content,
/// preview the selected content, restore into a new document.
/// </summary>
public sealed class HistoryPickerWindow : Form
{
    private const int MaxPreviewCharacters = 200_000;

    private readonly TextBox filterBox;
    private readonly ListView snapshotList;
    private readonly TextBox previewBox;
    private readonly IReadOnlyList<EditorSnapshot> snapshots;

    public HistoryPickerWindow(IReadOnlyList<EditorSnapshot> snapshots)
    {
        this.snapshots = snapshots;
        Text = SettingsAppearance.Text("Восстановить закрытый скрипт", "Restore Closed Script");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(760, 520);
        MinimumSize = new Size(520, 340);

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var cancel = new Button
        {
            Text = SettingsAppearance.Text("Отмена", "Cancel"),
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(650, 8)
        };
        var restore = new Button
        {
            Text = SettingsAppearance.Text("Открыть в новом документе", "Open in a new document"),
            Size = new Size(200, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(440, 8)
        };
        restore.Click += (_, _) => Accept();
        buttons.Controls.Add(restore);
        buttons.Controls.Add(cancel);

        previewBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9F)
        };
        var previewPanel = new Panel { Dock = DockStyle.Fill };
        previewPanel.Controls.Add(previewBox);
        previewPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = SettingsAppearance.Text("Содержимое снимка", "Snapshot content"),
            Padding = new Padding(8, 6, 0, 0)
        });

        var listPanel = new Panel { Dock = DockStyle.Fill };
        snapshotList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false
        };
        snapshotList.Columns.Add(SettingsAppearance.Text("Когда", "When"), 130);
        snapshotList.Columns.Add(SettingsAppearance.Text("Имя", "Name"), 170);
        snapshotList.Columns.Add(SettingsAppearance.Text("Путь", "Path"), 380);
        snapshotList.Columns.Add(SettingsAppearance.Text("Сервер", "Server"), 160);
        snapshotList.Columns.Add(SettingsAppearance.Text("База данных", "Database"), 160);
        snapshotList.SelectedIndexChanged += (_, _) => ShowPreview();
        snapshotList.DoubleClick += (_, _) => Accept();
        snapshotList.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                Accept();
                args.Handled = true;
            }
        };
        filterBox = new TextBox { Dock = DockStyle.Top };
        filterBox.TextChanged += (_, _) => Repopulate();
        listPanel.Controls.Add(snapshotList);
        listPanel.Controls.Add(filterBox);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 260
        };
        split.Panel1.Controls.Add(listPanel);
        split.Panel2.Controls.Add(previewPanel);

        Controls.Add(split);
        Controls.Add(buttons);
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            Repopulate();
            filterBox.Focus();
        };
    }

    public EditorSnapshot? SelectedSnapshot { get; private set; }

    private void Repopulate()
    {
        snapshotList.BeginUpdate();
        try
        {
            snapshotList.Items.Clear();
            var query = filterBox.Text;
            foreach (var snapshot in snapshots)
            {
                if (query.Length > 0 &&
                    snapshot.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    snapshot.FilePath.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    snapshot.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (snapshot.Server ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (snapshot.Database ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var item = new ListViewItem(snapshot.TimestampUtc.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm")) { Tag = snapshot };
                item.SubItems.Add(snapshot.DisplayName);
                item.SubItems.Add(snapshot.FilePath);
                item.SubItems.Add(snapshot.Server ?? "");
                item.SubItems.Add(snapshot.Database ?? "");
                snapshotList.Items.Add(item);
            }

            if (snapshotList.Items.Count > 0)
            {
                snapshotList.Items[0].Selected = true;
            }
        }
        finally
        {
            snapshotList.EndUpdate();
        }
    }

    private void ShowPreview()
    {
        if (snapshotList.SelectedItems.Count == 0 ||
            snapshotList.SelectedItems[0].Tag is not EditorSnapshot snapshot)
        {
            previewBox.Text = string.Empty;
            return;
        }

        previewBox.Text = snapshot.Content.Length <= MaxPreviewCharacters
            ? snapshot.Content
            : snapshot.Content.Substring(0, MaxPreviewCharacters);
    }

    private void Accept()
    {
        if (snapshotList.SelectedItems.Count == 0 ||
            snapshotList.SelectedItems[0].Tag is not EditorSnapshot snapshot)
        {
            return;
        }

        SelectedSnapshot = snapshot;
        DialogResult = DialogResult.OK;
        Close();
    }
}
