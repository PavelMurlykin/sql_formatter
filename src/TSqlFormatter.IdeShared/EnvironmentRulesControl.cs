using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TSqlFormatter.Configuration;

namespace TSqlFormatter.IdeShared;

internal sealed class EnvironmentRulesControl : UserControl
{
    private readonly DataGridView grid = new()
    {
        Width = 740, Height = 180, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoGenerateColumns = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AccessibleName = "EnvironmentRules"
    };
    internal EnvironmentRulesControl()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Controls.Add(flow);
        flow.Controls.Add(new Label { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Margin = new Padding(0, 18, 0, 8), AccessibleName = "EnvironmentRulesTitle",
            Text = T("Окружения DEV / TEST / PROD", "DEV / TEST / PROD environments") });
        flow.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(740, 0), Text = T(
            "Все заполненные условия строки должны совпасть (подстроки без учёта регистра). При пересечении: PROD > TEST > DEV. Без совпадений — окружение не определено.",
            "All filled fields in a row must match (case-insensitive substrings). Overlap: PROD > TEST > DEV. No match means unknown environment.") });
        var kind = new DataGridViewComboBoxColumn { Name = "Environment", HeaderText = T("Окружение", "Environment"), Width = 100 };
        kind.Items.AddRange("DEV", "TEST", "PROD"); grid.Columns.Add(kind);
        grid.Columns.Add("Server", T("Сервер содержит", "Server contains"));
        grid.Columns.Add("Database", T("База содержит", "Database contains"));
        grid.Columns.Add("Document", T("Путь / moniker содержит", "Path / moniker contains"));
        grid.Columns.Add("Color", T("Цвет #RRGGBB", "Color #RRGGBB"));
        foreach (DataGridViewColumn column in grid.Columns) if (column.Index > 0) column.Width = 150;
        grid.DataError += (_, args) => { args.ThrowException = false; };
        flow.Controls.Add(grid);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var add = new Button { AutoSize = true, Text = T("Добавить правило", "Add rule"), AccessibleName = "AddEnvironmentRule" };
        add.Click += (_, _) =>
        {
            if (grid.Rows.Count >= 100) return;
            int index = grid.Rows.Add("DEV", "", "", "", SqlEnvironmentRule.DefaultColor(SqlEnvironmentKind.Dev));
            grid.CurrentCell = grid.Rows[index].Cells[1]; grid.BeginEdit(true);
        };
        var remove = new Button { AutoSize = true, Text = T("Удалить выбранное", "Remove selected"), AccessibleName = "RemoveEnvironmentRule" };
        remove.Click += (_, _) => { if (grid.CurrentRow != null) grid.Rows.Remove(grid.CurrentRow); };
        buttons.Controls.Add(add); buttons.Controls.Add(remove); flow.Controls.Add(buttons);
    }
    internal void LoadSettings(SqlEnvironmentConfiguration settings)
    {
        grid.Rows.Clear();
        foreach (var rule in settings.Rules)
            grid.Rows.Add(rule.Label, rule.ServerContains, rule.DatabaseContains, rule.DocumentContains, rule.Color);
    }
    internal SqlEnvironmentConfiguration Settings
    {
        get
        {
            if (!grid.EndEdit()) throw new ArgumentException(T("Завершите редактирование правила окружения.", "Finish editing the environment rule."));
            var rules = grid.Rows.Cast<DataGridViewRow>().Select(row =>
            {
                try
                {
                    string Cell(int index) => Convert.ToString(row.Cells[index].Value) ?? "";
                    var kind = Cell(0) switch { "DEV" => SqlEnvironmentKind.Dev, "TEST" => SqlEnvironmentKind.Test,
                        "PROD" => SqlEnvironmentKind.Prod, _ => SqlEnvironmentKind.Unknown };
                    return new SqlEnvironmentRule(kind, Cell(1), Cell(2), Cell(3), Cell(4));
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException(string.Format(T(
                        "Правило окружения {0}: укажите DEV/TEST/PROD, хотя бы одно условие (до 1024 символов) и цвет #RRGGBB.",
                        "Environment rule {0}: specify DEV/TEST/PROD, at least one match field (up to 1024 characters) and a #RRGGBB color."), row.Index + 1), ex);
                }
            }).ToArray();
            return new(rules);
        }
    }
    private static string T(string ru, string en) => SettingsAppearance.Text(ru, en);
}
