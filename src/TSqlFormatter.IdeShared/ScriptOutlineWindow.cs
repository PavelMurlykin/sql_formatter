using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TSqlFormatter.Core.Navigation;

namespace TSqlFormatter.IdeShared;

/// <summary>Modeless script outline; double-click or Enter navigates to the node position.</summary>
public sealed class ScriptOutlineWindow : Form
{
    private readonly TreeView tree;

    public ScriptOutlineWindow(ScriptOutlineNode root)
    {
        Text = SettingsAppearance.Text("Структура скрипта", "Script Outline");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(420, 480);
        MinimumSize = new Size(280, 220);

        tree = new TreeView
        {
            Dock = DockStyle.Fill,
            ShowLines = true,
            HideSelection = false
        };
        AddNodes(tree.Nodes, root.Children);
        if (tree.Nodes.Count > 0)
        {
            tree.Nodes[0].Expand();
        }

        tree.DoubleClick += (_, _) => RaiseNavigate();
        tree.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                RaiseNavigate();
                args.Handled = true;
            }
        };

        var close = new Button
        {
            Dock = DockStyle.Bottom,
            Text = SettingsAppearance.Text("Закрыть", "Close")
        };
        close.Click += (_, _) => Close();

        Controls.Add(tree);
        Controls.Add(close);
        CancelButton = close;
    }

    public event Action<ScriptOutlineNode>? Navigate;

    private static void AddNodes(TreeNodeCollection parent, IEnumerable<ScriptOutlineNode> nodes)
    {
        foreach (var node in nodes)
        {
            var item = new TreeNode(node.Title) { Tag = node };
            AddNodes(item.Nodes, node.Children);
            parent.Add(item);
        }
    }

    private void RaiseNavigate()
    {
        if (tree.SelectedNode?.Tag is ScriptOutlineNode node)
        {
            Navigate?.Invoke(node);
        }
    }
}
