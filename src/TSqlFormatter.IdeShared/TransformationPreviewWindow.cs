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

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var cancel = new Button
        {
            Text = SettingsAppearance.Text("Отмена", "Cancel"),
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(650, 8)
        };
        var apply = new Button
        {
            Text = SettingsAppearance.Text("Применить", "Apply"),
            DialogResult = DialogResult.OK,
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(550, 8)
        };
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        split.Panel1.Controls.Add(CreateText(SettingsAppearance.Text("Было", "Before"), before));
        split.Panel2.Controls.Add(CreateText(SettingsAppearance.Text("Станет", "After"), after));

        Controls.Add(split);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = apply;
    }

    private static Control CreateText(string header, string text) => new Panel
    {
        Dock = DockStyle.Fill,
        Controls =
        {
            new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = header,
                Padding = new Padding(8, 6, 0, 0)
            },
            new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Text = text,
                Font = new Font(FontFamily.GenericMonospace, 9F)
            }
        }
    };
}
