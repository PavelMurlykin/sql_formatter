using System;
using System.Drawing;
using System.Windows.Forms;
using TSqlFormatter.Core.Refactoring;

namespace TSqlFormatter.IdeShared;

/// <summary>Asks for the new symbol name and validates it by symbol kind before accepting.</summary>
public sealed class RenamePromptWindow : Form
{
    private readonly TextBox nameBox;
    private readonly Label errorLabel;
    private readonly Func<string?, bool> validator;
    private readonly string validationError;

    public RenamePromptWindow(string currentName, RenameSymbolKind kind)
    {
        validator = kind == RenameSymbolKind.LocalVariable
            ? RenameNames.IsValidVariableName
            : RenameNames.IsValidIdentifier;
        validationError = kind == RenameSymbolKind.LocalVariable
            ? SettingsAppearance.Text(
                "Имя переменной: @ и идентификатор (буква/_/#, затем буквы, цифры, _, #), не более 128 символов вместе с @.",
                "A variable name is @ plus an identifier (letter/_/#, then letters, digits, _, #), at most 128 characters including @.")
            : SettingsAppearance.Text(
                "Алиас — идентификатор без @, не более 128 символов; ключевые слова SQL недопустимы.",
                "An alias is a plain identifier without @, at most 128 characters; reserved SQL words are not allowed.");

        Text = SettingsAppearance.Text("Переименование: ", "Rename: ") + currentName;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(480, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var current = new Label
        {
            Dock = DockStyle.Top,
            Text = SettingsAppearance.Text("Текущее имя: ", "Current name: ") + currentName,
            Height = 24,
            Padding = new Padding(8, 8, 0, 0)
        };
        errorLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            ForeColor = Color.Firebrick,
            Padding = new Padding(8, 4, 8, 0)
        };
        nameBox = new TextBox { Dock = DockStyle.Top, Text = currentName };
        nameBox.TextChanged += (_, _) => errorLabel.Text = string.Empty;

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var cancel = new Button
        {
            Text = SettingsAppearance.Text("Отмена", "Cancel"),
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(370, 8)
        };
        var accept = new Button
        {
            Text = SettingsAppearance.Text("Далее", "Next"),
            Size = new Size(90, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(270, 8)
        };
        accept.Click += (_, _) => Accept();
        buttons.Controls.Add(accept);
        buttons.Controls.Add(cancel);

        Controls.Add(nameBox);
        Controls.Add(current);
        Controls.Add(errorLabel);
        Controls.Add(buttons);
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            nameBox.SelectAll();
            nameBox.Focus();
        };
    }

    /// <summary>The validated new name after the dialog was accepted.</summary>
    public string NewName { get; private set; } = string.Empty;

    private void Accept()
    {
        var candidate = nameBox.Text.Trim();
        if (!validator(candidate))
        {
            errorLabel.Text = validationError;
            return;
        }

        NewName = candidate;
        DialogResult = DialogResult.OK;
        Close();
    }
}
