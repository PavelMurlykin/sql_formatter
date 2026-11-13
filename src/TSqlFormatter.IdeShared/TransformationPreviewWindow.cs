using System;
using System.Drawing;
using System.Windows.Forms;

namespace TSqlFormatter.IdeShared;

/// <summary>Before/after preview of a transformation; Apply returns DialogResult.OK.</summary>
public sealed class TransformationPreviewWindow : Form
{
    public TransformationPreviewWindow(string title, string before, string after)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(760, 460);
        MinimumSize = new Size(520, 300);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6), WrapContents = false };
        var cancel = new Button
        {
            Text = SettingsAppearance.Text("Отмена", "Cancel"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true, MinimumSize = new Size(90, 28)
        };
        var apply = new Button
        {
            Text = SettingsAppearance.Text("Применить", "Apply"),
            DialogResult = DialogResult.OK,
            AutoSize = true, MinimumSize = new Size(90, 28)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(740, 360), SplitterDistance = 360, Panel1MinSize = 130, Panel2MinSize = 130
        };
        split.Panel1.Controls.Add(CreateText(SettingsAppearance.Text("Было", "Before"), before));
        split.Panel2.Controls.Add(CreateText(SettingsAppearance.Text("Станет", "After"), after));

        Controls.Add(split);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = apply;
    }

    private static Control CreateText(string header, string text)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        // Dock the header first, then fill the remaining space. The first SQL line
        // must not sit underneath the header (notably for one-line IN fragments).
        panel.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Text = text, Font = new Font(FontFamily.GenericMonospace, 9F),
            AccessibleName = header
        });
        panel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = header, Padding = new Padding(8, 6, 0, 0) });
        return panel;
    }
}
