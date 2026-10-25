using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TSqlFormatter.Core.Snippets;

namespace TSqlFormatter.IdeShared;

/// <summary>Modal picker over the available snippets; Enter or double-click selects one.</summary>
public sealed class SnippetPickerWindow : Form
{
    private readonly TextBox filterBox;
    private readonly ListView snippetList;
    private readonly IReadOnlyList<SqlSnippet> snippets;

    public SnippetPickerWindow(IReadOnlyList<SqlSnippet> snippets)
    {
        this.snippets = snippets;
        Text = SettingsAppearance.Text("Вставка сниппета SQL", "Insert SQL Snippet");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(580, 380);
        MinimumSize = new Size(420, 260);

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var cancel = new Button
        {
            Text = SettingsAppearance.Text("Отмена", "Cancel"),
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(470, 8)
        };
        var insert = new Button
        {
            Text = SettingsAppearance.Text("Вставить", "Insert"),
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(370, 8)
        };
        insert.Click += (_, _) => Accept();
        buttons.Controls.Add(insert);
        buttons.Controls.Add(cancel);

        filterBox = new TextBox { Dock = DockStyle.Top };
        filterBox.TextChanged += (_, _) => Repopulate();

        snippetList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false
        };
        snippetList.Columns.Add(SettingsAppearance.Text("Ярлык", "Shortcut"), 90);
        snippetList.Columns.Add(SettingsAppearance.Text("Имя", "Name"), 170);
        snippetList.Columns.Add(SettingsAppearance.Text("Описание", "Description"), 270);
        snippetList.DoubleClick += (_, _) => Accept();
        snippetList.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                Accept();
                args.Handled = true;
            }
        };

        Controls.Add(snippetList);
        Controls.Add(filterBox);
        Controls.Add(buttons);
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            Repopulate();
            filterBox.Focus();
        };
    }

    public SqlSnippet? SelectedSnippet { get; private set; }

    private void Repopulate()
    {
        snippetList.BeginUpdate();
        try
        {
            snippetList.Items.Clear();
            var filter = filterBox.Text.Trim();
            foreach (var snippet in snippets)
            {
                if (filter.Length > 0 &&
                    !Contains(snippet.Shortcut, filter) && !Contains(snippet.Name, filter) &&
                    !(snippet.Description is { } description && Contains(description, filter)))
                {
                    continue;
                }

                var item = new ListViewItem(snippet.Shortcut) { Tag = snippet };
                item.SubItems.Add(snippet.Name);
                item.SubItems.Add(snippet.Description ?? string.Empty);
                snippetList.Items.Add(item);
            }

            if (snippetList.Items.Count > 0)
            {
                snippetList.Items[0].Selected = true;
            }
        }
        finally
        {
            snippetList.EndUpdate();
        }
    }

    private static bool Contains(string text, string filter) =>
        text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    private void Accept()
    {
        if (snippetList.SelectedItems.Count == 0 ||
            snippetList.SelectedItems[0].Tag is not SqlSnippet snippet)
        {
            return;
        }

        SelectedSnippet = snippet;
        DialogResult = DialogResult.OK;
        Close();
    }
}
