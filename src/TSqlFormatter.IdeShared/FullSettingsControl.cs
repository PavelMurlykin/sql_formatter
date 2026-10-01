using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.IdeShared;

internal sealed class FullSettingsControl : UserControl
{
    private readonly Func<string?> sqlPathProvider;
    private readonly SettingsPresentation presentation = new();
    private readonly CheckBox enabled = new() { Text = "Использовать эти настройки при форматировании", AutoSize = true };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, AccessibleName = "Поиск настроек" };
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true };
    private readonly Label heading = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly FlowLayoutPanel editors = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly Label description = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly TextBox source = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly RichTextBox sample = SqlBox(false);
    private readonly RichTextBox result = SqlBox(true);
    private readonly CheckBox projectPreview = new() { Text = "Учитывать настройки проекта активного SQL-файла", AutoSize = true };
    private readonly ComboBox profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 450, DropDownWidth = 650 };
    private readonly TextBox profileName = new() { Width = 280, MaxLength = 100, AccessibleName = "Имя нового профиля" };
    private readonly Label defaultProfile = new() { AutoSize = true, MaximumSize = new Size(560, 0) };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };
    private CancellationTokenSource? previewCancellation;
    private SettingsField[] selected = Array.Empty<SettingsField>();
    private int revision;
    private bool loading;
    private bool running;
    private bool customSample;

    internal FullSettingsControl(Func<string?> sqlPathProvider)
    {
        this.sqlPathProvider = sqlPathProvider;
        Model = new SettingsEditorModel();
        Profiles = new UserProfileStore();
        Size = new Size(1000, 660);
        AutoScaleMode = AutoScaleMode.Font;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(enabled, 0, 0);
        layout.Controls.Add(tabs, 0, 1);
        layout.Controls.Add(source, 0, 2);
        layout.Controls.Add(status, 0, 3);
        Controls.Add(layout);
        var settingsTab = new TabPage("Настройки форматирования");
        var profilesTab = new TabPage("Сохранённые профили");
        tabs.TabPages.AddRange(new[] { settingsTab, profilesTab });
        var navigation = new SplitContainer { Size = new Size(980, 600), Dock = DockStyle.Fill, SplitterDistance = 260, Panel1MinSize = 180, Panel2MinSize = 220 };
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(new Label { Text = "Поиск по названию или ключевому слову", AutoSize = true }, 0, 0);
        left.Controls.Add(search, 0, 1);
        left.Controls.Add(tree, 0, 2);
        navigation.Panel1.Controls.Add(left);
        var right = new SplitContainer { Size = new Size(700, 600), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 210, Panel1MinSize = 90, Panel2MinSize = 100 };
        var settings = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
        settings.Controls.Add(description); settings.Controls.Add(editors); settings.Controls.Add(heading);
        settings.SizeChanged += (_, _) => heading.MaximumSize = description.MaximumSize = new Size(Math.Max(120, settings.ClientSize.Width - 28), 0);
        right.Panel1.Controls.Add(settings);
        var preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        preview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        preview.Controls.Add(Row(Button("Форматировать пример", FormatSample), Button("Пример выбранной настройки", UseExample)), 0, 0);
        preview.Controls.Add(projectPreview, 0, 1);
        var code = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        code.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        code.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        code.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        code.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        code.Controls.Add(new Label { Text = "Исходный SQL — можно ввести свой код", AutoSize = true }, 0, 0);
        code.Controls.Add(new Label { Text = "Отформатированный SQL", AutoSize = true }, 1, 0);
        code.Controls.Add(sample, 0, 1); code.Controls.Add(result, 1, 1);
        preview.Controls.Add(code, 0, 2);
        right.Panel2.Controls.Add(preview);
        navigation.Panel2.Controls.Add(right);
        settingsTab.Controls.Add(navigation);
        var profileLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        profileLayout.Controls.Add(defaultProfile);
        profileLayout.Controls.Add(new Label { Text = "Встроенные и ваши сохранённые стили", AutoSize = true });
        profileLayout.Controls.Add(profiles);
        profileLayout.Controls.Add(Row(Button("Загрузить в настройки", LoadProfile), Button("Использовать по умолчанию", MakeDefault)));
        profileLayout.Controls.Add(new Label { Text = "Сохранить текущие настройки в новый профиль", AutoSize = true });
        profileLayout.Controls.Add(profileName);
        profileLayout.Controls.Add(Button("Сохранить профиль", SaveProfile));
        profileLayout.Controls.Add(Row(Button("Импорт JSON…", Import), Button("Экспорт JSON…", Export)));
        profileLayout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(560, 0), Text =
            "OK сохраняет настройки, профили и выбор по умолчанию. Отмена отбрасывает изменения. " +
            "Редактирование настройки переключает профиль по умолчанию на текущие настройки. " +
            "Экспорт записывает файл сразу и не отменяется кнопкой Отмена." });
        profilesTab.Controls.Add(profileLayout);
        sample.MaxLength = 8192;
        sample.AccessibleName = "Исходный SQL";
        result.AccessibleName = "Отформатированный SQL";
        enabled.CheckedChanged += (_, _) => QueuePreview();
        search.TextChanged += (_, _) => Filter();
        tree.AfterSelect += (_, _) => SelectGroup();
        sample.TextChanged += (_, _) =>
        {
            if (loading) return;
            customSample = true;
            InvalidatePreview();
            status.Text = "Код изменён. Нажмите «Форматировать пример» (Ctrl+Enter).";
        };
        sample.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FormatSample(); } };
        projectPreview.CheckedChanged += (_, _) => QueuePreview();
        debounce.Tick += (_, _) => RefreshPreview();
    }

    internal SettingsEditorModel Model { get; private set; }
    internal UserProfileStore Profiles { get; private set; }
    internal bool UseFullSettings => enabled.Checked;
    internal void ShowProfiles() => tabs.SelectedIndex = 1;
    internal void LoadDraft(FormattingOptions options, UserProfileStore store, bool useFullSettings)
    {
        loading = true;
        Model = new SettingsEditorModel(options); Profiles = store;
        enabled.Checked = useFullSettings; search.Clear(); customSample = false;
        RefreshProfiles(); loading = false; Filter();
    }
    private void Filter()
    {
        if (loading) return;
        string? previous = selected.FirstOrDefault() is { } previousField ? presentation.GroupId(previousField) : null;
        tree.BeginUpdate(); tree.Nodes.Clear();
        TreeNode? first = null, restore = null;
        var allGroups = Model.Fields.GroupBy(presentation.GroupId).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var group in presentation.Find(Model, search.Text).GroupBy(presentation.GroupId))
        {
            var field = group.First();
            var collection = tree.Nodes;
            string pathKey = "";
            foreach (var segment in presentation.Path(field))
            {
                pathKey += "/" + segment;
                var parent = collection.Cast<TreeNode>().FirstOrDefault(n => n.Name == pathKey);
                if (parent is null) { parent = new TreeNode(segment) { Name = pathKey }; collection.Add(parent); }
                collection = parent.Nodes;
            }
            var node = new TreeNode(presentation.Title(field)) { Name = group.Key,
                Tag = allGroups[group.Key], ToolTipText = presentation.Context(field) };
            collection.Add(node); first ??= node;
            if (group.Key == previous) restore = node;
        }
        if (!string.IsNullOrWhiteSpace(search.Text)) tree.ExpandAll();
        tree.SelectedNode = restore ?? first; tree.SelectedNode?.EnsureVisible(); tree.EndUpdate();
        if (first is null)
        {
            InvalidatePreview();
            selected = Array.Empty<SettingsField>(); ClearEditors();
            heading.Text = "Настройки не найдены — измените запрос поиска."; description.Text = "";
            status.Text = "Настройки не найдены.";
        }
    }
    private void SelectGroup()
    {
        if (tree.SelectedNode?.Tag is not SettingsField[] fields)
        {
            var node = tree.SelectedNode;
            while (node?.Nodes.Count > 0) node = node.Nodes[0];
            if (node?.Tag is SettingsField[]) tree.SelectedNode = node;
            return;
        }
        selected = fields; ShowFields();
        if (!customSample) UseExample(); else QueuePreview();
    }
    private void ClearEditors()
    {
        foreach (Control control in editors.Controls.Cast<Control>().ToArray()) control.Dispose();
        editors.Controls.Clear();
    }
    private void ShowFields()
    {
        loading = true; editors.SuspendLayout(); ClearEditors();
        if (selected.Length == 0) { loading = false; editors.ResumeLayout(); return; }
        heading.Text = presentation.Context(selected[0]);
        description.Text = presentation.Describe(Model, selected[0]);
        foreach (var field in selected)
        {
            string label = presentation.FieldLabel(field);
            object current = Model.Get(field.Id);
            Control editor;
            if (field.Kind == SettingsFieldKind.Boolean)
            {
                var check = new CheckBox { Text = label, AutoSize = true, Checked = (bool)current, AccessibleName = label };
                check.CheckedChanged += (_, _) => Change(field, check.Checked);
                editor = check;
            }
            else if (field.Kind == SettingsFieldKind.Integer)
            {
                var number = new NumericUpDown { Minimum = field.Minimum, Maximum = field.Maximum, Value = (int)current, Width = 140, AccessibleName = label };
                number.ValueChanged += (_, _) => Change(field, decimal.ToInt32(number.Value));
                editor = Row(new Label { Text = label + $" ({field.Minimum}…{field.Maximum})", AutoSize = true }, number);
            }
            else
            {
                var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = label };
                choice.Items.AddRange(field.Choices.Select(v => new Choice(v, presentation.ChoiceLabel(v))).Cast<object>().ToArray());
                choice.SelectedIndex = field.Choices.ToList().IndexOf((string)current);
                choice.SelectedIndexChanged += (_, _) => { if (choice.SelectedItem is Choice value) Change(field, value.Value); };
                editor = Row(new Label { Text = label, AutoSize = true }, choice);
            }
            editors.Controls.Add(editor);
        }
        editors.Controls.Add(Row(Button("Сбросить выбранное правило", ResetGroup), Button("Сбросить все настройки", ResetAll)));
        editors.ResumeLayout(); loading = false;
    }
    private void Change(SettingsField field, object value)
    {
        if (loading) return;
        Guard(() =>
        {
            Model.Set(field.Id, value); Profiles.SetDefault(null); enabled.Checked = true;
            description.Text = presentation.Describe(Model, field); RefreshDefaultLabel(); QueuePreview();
        });
    }
    private void ResetGroup() => Guard(() =>
    {
        foreach (var field in selected) Model.Reset(field.Id);
        Profiles.SetDefault(null); enabled.Checked = true; RefreshDefaultLabel(); ShowFields(); QueuePreview();
    });
    private void ResetAll() => Guard(() =>
    {
        if (!Confirm("Сбросить все настройки? Сохранённые профили останутся.")) return;
        Model.ResetAll(); Profiles.SetDefault(null); enabled.Checked = true; RefreshDefaultLabel(); ShowFields(); QueuePreview();
    });
    private void RefreshProfiles()
    {
        string? previous = (profiles.SelectedItem as ProfileItem)?.Profile.Id ?? Profiles.DefaultProfileId;
        profiles.Items.Clear();
        profiles.Items.AddRange(Profiles.AvailableProfiles.Select(p => new ProfileItem(p,
            (p.Id.StartsWith("user:", StringComparison.Ordinal) ? "Мой профиль: " : "Встроенный: ") + presentation.ProfileName(p))).Cast<object>().ToArray());
        profiles.SelectedIndex = Math.Max(0, profiles.Items.Cast<ProfileItem>().ToList().FindIndex(p => p.Profile.Id == previous));
        RefreshDefaultLabel();
    }
    private void RefreshDefaultLabel()
    {
        var profile = Profiles.AvailableProfiles.FirstOrDefault(p => p.Id == Profiles.DefaultProfileId);
        defaultProfile.Text = "По умолчанию: " + (profile is null ? "текущие настройки" : presentation.ProfileName(profile));
    }
    private void SaveProfile() => Guard(() =>
    {
        string name = profileName.Text.Trim();
        if (name.Length is 0 or > 100) throw new ArgumentException("Введите имя профиля (1–100 символов).");
        bool replace = Profiles.Contains(name);
        if (replace && !Confirm("Заменить сохранённый профиль «" + name + "»?")) return;
        Profiles.Save(name, Model.Options, replace); RefreshProfiles();
        profiles.SelectedIndex = profiles.Items.Cast<ProfileItem>().ToList().FindIndex(p => string.Equals(p.Profile.Id, "user:" + name, StringComparison.OrdinalIgnoreCase));
        status.Text = "Профиль сохранён в черновике. Нажмите OK для сохранения в IDE.";
    });
    private void LoadProfile() => Guard(() =>
    {
        if (profiles.SelectedItem is not ProfileItem item) return;
        Model = new SettingsEditorModel(item.Profile.Options); Profiles.SetDefault(null);
        enabled.Checked = true; RefreshDefaultLabel(); ShowFields(); QueuePreview();
    });
    private void MakeDefault() => Guard(() =>
    {
        if (profiles.SelectedItem is not ProfileItem item) return;
        Profiles.SetDefault(item.Profile.Id); Model = new SettingsEditorModel(Profiles.ResolveDefault(Model.Options));
        enabled.Checked = true; RefreshDefaultLabel(); ShowFields(); QueuePreview();
        status.Text = "Профиль выбран по умолчанию. Нажмите OK, чтобы применить выбор.";
    });
    private void Import() => Guard(() =>
    {
        using var dialog = new OpenFileDialog { Filter = "Настройки SQL Formatter (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var imported = new SqlFormatterProfileExchange().Import(dialog.FileName);
        if (!imported.Succeeded) throw new InvalidOperationException(imported.Diagnostics[0].Message);
        Model = new SettingsEditorModel(imported.Options!);
        Profiles.SetDefault(null); enabled.Checked = true; RefreshDefaultLabel(); ShowFields(); QueuePreview();
    });
    private void Export() => Guard(() =>
    {
        using var dialog = new SaveFileDialog { Filter = "Настройки SQL Formatter (*.json)|*.json", DefaultExt = "json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) new SqlFormatterProfileExchange().Export(dialog.FileName, Model.Options);
    });
    private void UseExample()
    {
        if (selected.Length == 0) return;
        loading = true; sample.Text = presentation.Example(selected[0]); loading = false;
        customSample = false; QueuePreview();
    }
    private void FormatSample() { InvalidatePreview(); RefreshPreview(); }
    private void InvalidatePreview() { revision++; previewCancellation?.Cancel(); debounce.Stop(); result.Clear(); }
    private void QueuePreview()
    {
        if (loading || IsDisposed) return;
        InvalidatePreview(); debounce.Start();
    }
    private void RefreshPreview()
    {
        debounce.Stop();
        if (!IsHandleCreated || IsDisposed) return;
        if (running) { debounce.Start(); return; }
        string sql = sample.Text;
        string? path;
        try { path = projectPreview.Checked ? sqlPathProvider() : null; }
        catch (Exception ex) { status.Text = ex.Message; return; }
        if (projectPreview.Checked && string.IsNullOrEmpty(path))
        { source.Text = "Нет активного сохранённого SQL-файла — настройки проекта недоступны."; return; }
        int currentRevision = revision;
        var options = Model.Options;
        var cancellation = previewCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        running = true; status.Text = "Форматирование примера… SQL-документ IDE не изменяется.";
        _ = Task.Run(() => SettingsPreview.Format(sql, options, path, cancellation.Token), cancellation.Token).ContinueWith(task =>
        {
            running = false;
            if (ReferenceEquals(previewCancellation, cancellation)) previewCancellation = null;
            cancellation.Dispose();
            if (IsDisposed || currentRevision != revision) return;
            result.Clear();
            if (task.IsCanceled) { status.Text = "Форматирование отменено или превысило 2 секунды."; return; }
            if (task.IsFaulted) { status.Text = "Ошибка примера: " + task.Exception?.GetBaseException().Message; return; }
            source.Text = "Источник: " + task.Result.Source;
            var error = task.Result.Diagnostics.FirstOrDefault(d => d.Severity == FormatterDiagnosticSeverity.Error);
            if (error is null && task.Result.Result is { } formatted)
            {
                result.Text = formatted.Text; // RichTextBox supports LF without display-only EOL conversion.
                status.Text = "Пример отформатирован. " + (enabled.Checked ? "Настройки сохранятся после OK." : "Для команд IDE включите использование этих настроек.");
            }
            else status.Text = error is null ? "Пример не удалось отформатировать." : error.Code + ": " + error.Message;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
    internal void StopPreview() { revision++; debounce.Stop(); previewCancellation?.Cancel(); }
    private void Guard(Action action)
    { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "SQL Formatter", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private bool Confirm(string message) => MessageBox.Show(this, message, "SQL Formatter", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    private static FlowLayoutPanel Row(params Control[] controls)
    { var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top }; row.Controls.AddRange(controls); return row; }
    private static Button Button(string text, Action action)
    { var button = new Button { Text = text, AutoSize = true }; button.Click += (_, _) => action(); return button; }
    private static RichTextBox SqlBox(bool readOnly) => new()
    { Dock = DockStyle.Fill, ReadOnly = readOnly, WordWrap = false, DetectUrls = false, AcceptsTab = true,
        ScrollBars = RichTextBoxScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 10) };
    protected override void Dispose(bool disposing)
    { if (disposing) { StopPreview(); debounce.Dispose(); } base.Dispose(disposing); }
    private sealed class Choice
    { public Choice(string value, string label) { Value = value; Label = label; } public string Value { get; } public string Label { get; } public override string ToString() => Label; }
    private sealed class ProfileItem
    { public ProfileItem(FormattingProfile profile, string label) { Profile = profile; Label = label; } public FormattingProfile Profile { get; } public string Label { get; } public override string ToString() => Label; }
}
