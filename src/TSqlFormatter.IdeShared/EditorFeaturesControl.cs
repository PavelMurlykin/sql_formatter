using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Analysis;

namespace TSqlFormatter.IdeShared;

/// <summary>Transactional controls for analysis defaults and local history, shared by all four adapters.</summary>
internal sealed class EditorFeaturesControl : UserControl
{
    private readonly CheckBox analysis = new() { AutoSize = true, AccessibleName = "analysis.enabled" };
    private readonly CheckBox history = new() { AutoSize = true, AccessibleName = "history.enabled" };
    private readonly NumericUpDown count = Number("history.maxSnapshots", 1, 10000);
    private readonly NumericUpDown characters = Number("history.maxCharacters", 1, 16 * 1024 * 1024);
    private readonly NumericUpDown age = Number("history.maxAgeDays", 0, 36500);
    private readonly Dictionary<string, (CheckBox Enabled, ComboBox Severity)> rules = new();
    private AnalysisConfiguration original = AnalysisConfiguration.Default;

    internal EditorFeaturesControl()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AccessibleName = "EditorFeatures";
        var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Controls.Add(flow);
        flow.Controls.Add(Title(T("Анализ SQL", "SQL analysis")));
        analysis.Text = T("Включить анализ по команде", "Enable analysis on command"); flow.Controls.Add(analysis);
        flow.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(760, 0), Text = T(
            "Настройки проекта analysis перекрывают указанные здесь значения. Анализ при наборе текста не выполняется.",
            "Project analysis settings override these defaults. Analysis does not run while typing.") });
        var descriptions = new[] {
            T("SELECT *", "SELECT *"), T("INSERT без списка колонок", "INSERT without column list"),
            T("TOP без ORDER BY", "TOP without ORDER BY"), T("UPDATE без WHERE", "UPDATE without WHERE"),
            T("DELETE без WHERE", "DELETE without WHERE") };
        int index = 0;
        foreach (var rule in SqlAnalyzer.DefaultRules)
        {
            var enabled = new CheckBox { AutoSize = false, Width = 450, Height = 27,
                Text = rule.Code + " — " + descriptions[index++], AccessibleName = "analysis." + rule.Code + ".enabled" };
            var severity = new SettingsComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "analysis." + rule.Code + ".severity" };
            severity.Items.AddRange(new object[] { T("По умолчанию", "Default"), T("Информация", "Info"), T("Предупреждение", "Warning"), T("Ошибка", "Error") });
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(enabled); row.Controls.Add(severity); flow.Controls.Add(row);
            rules.Add(rule.Code, (enabled, severity));
        }
        flow.Controls.Add(Title(T("Локальная история SQL", "Local SQL history")));
        history.Text = T("Сохранять снимки при сохранении и закрытии SQL-документов", "Keep snapshots on save and close of SQL documents");
        flow.Controls.Add(history);
        flow.Controls.Add(Row(T("Максимум снимков", "Maximum snapshots"), count));
        flow.Controls.Add(Row(T("Максимум символов в снимке", "Maximum characters per snapshot"), characters));
        flow.Controls.Add(Row(T("Срок хранения, дней (0 — без срока)", "Retention days (0 — no age limit)"), age));
        flow.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(760, 0), Text = T(
            "Данные остаются локально. Уменьшение лимитов применяется при следующем снимке или открытии истории; отключение сохраняет существующие файлы.",
            "Data stays local. Lower limits take effect on the next snapshot or opening history; disabling keeps existing files.") });
        LoadSettings(new EditorFeaturesSettings());
    }

    internal void LoadSettings(EditorFeaturesSettings settings)
    {
        original = settings.Analysis; analysis.Checked = original.Enabled;
        foreach (var pair in rules)
        {
            pair.Value.Enabled.Checked = !original.DisabledRules.Contains(pair.Key, StringComparer.OrdinalIgnoreCase);
            pair.Value.Severity.SelectedIndex = original.SeverityOverrides.TryGetValue(pair.Key, out var value) ? (int)value + 1 : 0;
        }
        history.Checked = settings.HistoryEnabled; count.Value = settings.HistoryMaxSnapshots;
        characters.Value = settings.HistoryMaxCharacters; age.Value = settings.HistoryMaxAgeDays;
    }

    internal EditorFeaturesSettings Settings
    {
        get
        {
            var disabled = original.DisabledRules.Where(code => !rules.ContainsKey(code)).ToList();
            var overrides = new Dictionary<string, AnalysisSeverity>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in original.SeverityOverrides) overrides[pair.Key] = pair.Value;
            foreach (var pair in rules)
            {
                if (!pair.Value.Enabled.Checked) disabled.Add(pair.Key);
                if (pair.Value.Severity.SelectedIndex == 0) overrides.Remove(pair.Key);
                else overrides[pair.Key] = (AnalysisSeverity)(pair.Value.Severity.SelectedIndex - 1);
            }
            return new EditorFeaturesSettings(new AnalysisConfiguration(disabled, overrides, analysis.Checked),
                history.Checked, (int)count.Value, (int)characters.Value, (int)age.Value);
        }
    }

    private static string T(string ru, string en) => SettingsAppearance.Text(ru, en);
    private static NumericUpDown Number(string name, int min, int max) => new()
    { AccessibleName = name, Minimum = min, Maximum = max, Width = 160 };
    private static Label Title(string text) => new() { AutoSize = true, Text = text,
        Font = new Font("Segoe UI", 11, FontStyle.Bold), Margin = new Padding(0, 18, 0, 8) };
    private static Control Row(string label, Control value)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = label, Width = 450, Padding = new Padding(0, 5, 0, 0) });
        row.Controls.Add(value); return row;
    }
}
