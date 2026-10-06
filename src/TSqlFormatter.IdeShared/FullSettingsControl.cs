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
    private readonly SettingsPresentation presentation = new(SettingsAppearance.Russian);
    private readonly TextBox search = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Margin = new Padding(2, 5, 2, 0), AccessibleName = "SettingsSearch" };
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, ShowRootLines = false, ShowPlusMinus = false, BorderStyle = BorderStyle.None, ItemHeight = 36, Indent = 22 };
    private readonly Font navigationGroupFont = new("Segoe UI", 9, FontStyle.Bold);
    private readonly Label heading = new() { AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), Margin = new Padding(0, 0, 0, 18) };
    private readonly FlowLayoutPanel editors = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18) };
    private readonly Label error = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(700, 0) };
    private readonly SqlPreviewBox sample = new(false) { MaxLength = 8192, AccessibleName = "SourceSQL" };
    private readonly SqlPreviewBox result = new(true) { AccessibleName = "FormattedSQL" };
    private readonly SettingsComboBox generalProfiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350, AccessibleName = "GeneralActiveProfile" };
    private readonly SettingsComboBox profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350, AccessibleName = "FormattingActiveProfile" };
    private readonly TextBox profileName = new() { Width = 350, MaxLength = 100, AccessibleName = "ActiveProfileName" };
    private readonly TextBox shortcut = new() { Width = 350, ReadOnly = true, AccessibleName = "FormatShortcut" };
    private readonly SettingsTabControl tabs = new() { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, ItemSize = new Size(180, 32), SizeMode = TabSizeMode.Fixed };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };
    private CancellationTokenSource? previewCancellation;
    private SettingsField[] selected = Array.Empty<SettingsField>();
    private SettingsAppearance appearance = SettingsAppearance.Light;
    private int revision;
    private bool loading, running, customSample, dirty;
    private string? activeId;

    internal FullSettingsControl(Func<string?> sqlPathProvider)
    {
        // Preview uses exactly the profile being edited. Project resolution belongs to document commands.
        _ = sqlPathProvider;
        Model = new SettingsEditorModel(); Profiles = new UserProfileStore();
        Size = new Size(1100, 750); Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(tabs);
        var generalTab = new TabPage(T("Общие", "General"));
        var formattingTab = new TabPage(T("Форматирование", "Formatting"));
        tabs.TabPages.AddRange(new[] { generalTab, formattingTab });
        var general = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
        general.Controls.Add(Title(T("Общие настройки профиля", "General profile settings")));
        general.Controls.Add(FieldRow(T("Горячие клавиши для форматирования", "Formatting shortcut"), shortcut));
        general.Controls.Add(FieldRow(T("Активный профиль", "Active profile"), generalProfiles));
        general.Controls.Add(FieldRow(T("Название активного профиля", "Active profile name"), profileName));
        general.Controls.Add(Row(Button(T("Импорт профиля…", "Import profile…"), Import), Button(T("Экспорт профиля…", "Export profile…"), Export)));
        general.Controls.Add(Button(T("Сохранить", "Save"), Save));
        generalTab.Controls.Add(general);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Row(new Label { Text = T("Активный профиль", "Active profile"), AutoSize = true, Padding = new Padding(0, 7, 8, 0) }, profiles, Button(T("Сохранить", "Save"), Save)), 0, 0);
        var navigation = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1050, 640), SplitterDistance = 240, Panel1MinSize = 180, Panel2MinSize = 340, SplitterWidth = 4 };
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 8), BorderStyle = BorderStyle.FixedSingle, AccessibleName = "SearchBox" };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(new Label { Text = "⌕", Font = new Font("Segoe UI Symbol", 15), AutoSize = true, AccessibleName = T("Поиск настроек", "Search options") }, 0, 0);
        searchRow.Controls.Add(search, 1, 0); left.Controls.Add(searchRow, 0, 0); left.Controls.Add(tree, 0, 1);
        navigation.Panel1.Controls.Add(left);
        var right = new SplitContainer { Size = new Size(780, 640), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 335, Panel1MinSize = 100, Panel2MinSize = 150, SplitterWidth = 5 };
        right.Panel1.Controls.Add(editors);
        var preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10, 0, 10, 4) };
        preview.RowStyles.Add(new RowStyle(SizeType.AutoSize)); preview.RowStyles.Add(new RowStyle(SizeType.AutoSize)); preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        preview.Controls.Add(Button(T("Форматировать пример", "Format example"), FormatSample), 0, 0); preview.Controls.Add(error, 0, 1);
        var code = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        code.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); code.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        code.RowStyles.Add(new RowStyle(SizeType.AutoSize)); code.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        code.Controls.Add(new Label { Text = T("Исходный SQL", "Source SQL"), AutoSize = true, Padding = new Padding(0, 6, 0, 6) }, 0, 0);
        code.Controls.Add(new Label { Text = T("Отформатированный SQL", "Formatted SQL"), AutoSize = true, Padding = new Padding(0, 6, 0, 6) }, 1, 0);
        code.Controls.Add(sample, 0, 1); code.Controls.Add(result, 1, 1); preview.Controls.Add(code, 0, 2);
        right.Panel2.Controls.Add(preview); navigation.Panel2.Controls.Add(right); layout.Controls.Add(navigation, 0, 1); formattingTab.Controls.Add(layout);
        search.TextChanged += (_, _) => Filter(); tree.AfterSelect += (_, _) => SelectGroup();
        profiles.SelectedIndexChanged += (_, _) => SelectProfile(profiles); generalProfiles.SelectedIndexChanged += (_, _) => SelectProfile(generalProfiles);
        profileName.TextChanged += (_, _) => { if (!loading) dirty = true; };
        sample.TextChanged += (_, _) => { if (!loading) { customSample = true; InvalidatePreview(); } };
        sample.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FormatSample(); } };
        shortcut.KeyDown += (_, e) =>
        {
            if (!e.Control && !e.Alt && e.KeyCode is Keys.Tab or Keys.Escape) return;
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.Back or Keys.Delete) { shortcut.Clear(); return; }
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;
            if (!e.Control && !e.Alt && (e.KeyCode < Keys.F1 || e.KeyCode > Keys.F24)) return;
            string key = e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9 ? ((int)e.KeyCode - (int)Keys.D0).ToString() : e.KeyCode.ToString();
            shortcut.Text = (e.Control ? "Ctrl+" : "") + (e.Alt ? "Alt+" : "") + (e.Shift ? "Shift+" : "") + key;
        };
        debounce.Tick += (_, _) => RefreshPreview();
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedIndex == 1) QueuePreview(); };
        ApplyAppearance(appearance);
    }
    private string T(string ru, string en) => presentation.Text(ru, en);
    internal SettingsEditorModel Model { get; private set; }
    internal UserProfileStore Profiles { get; private set; }
    internal bool UseFullSettings => true;
    internal string Shortcut { get => shortcut.Text; set => shortcut.Text = value; }
    internal event Action? SaveRequested;
    internal void ShowProfiles() => tabs.SelectedIndex = 0;
    internal void ApplyAppearance(SettingsAppearance value)
    {
        appearance = value; appearance.Apply(this); sample.ApplyAppearance(value); result.ApplyAppearance(value); tabs.Accent = value.Accent; tabs.Invalidate();
    }
    internal void LoadDraft(FormattingOptions options, UserProfileStore store, bool useFullSettings)
    {
        _ = useFullSettings; loading = true;
        Model = new SettingsEditorModel(options); Profiles = store; activeId = store.DefaultProfileId;
        search.Clear(); customSample = false; dirty = false; RefreshProfiles(); loading = false; Filter();
    }
    private void Filter()
    {
        if (loading) return;
        string? previous = selected.FirstOrDefault() is { } f ? presentation.PageId(f) : null;
        tree.BeginUpdate(); tree.Nodes.Clear(); TreeNode? first = null, restore = null;
        var all = Model.Fields.GroupBy(presentation.PageId).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var group in presentation.Find(Model, search.Text).GroupBy(presentation.PageId).OrderBy(g => presentation.PageOrder(g.First())))
        {
            var field = group.First(); var path = presentation.PagePath(field); string groupId = group.Key.Split('.')[0];
            var header = tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Name == groupId);
            if (header is null) { header = new TreeNode(path[0]) { Name = groupId, NodeFont = navigationGroupFont }; tree.Nodes.Add(header); }
            var node = new TreeNode(path[1]) { Name = group.Key, Tag = all[group.Key] }; header.Nodes.Add(node);
            first ??= node; if (group.Key == previous) restore = node;
        }
        tree.ExpandAll(); tree.SelectedNode = restore ?? first; tree.SelectedNode?.EnsureVisible(); tree.EndUpdate();
        if (first is null) { InvalidatePreview(); selected = Array.Empty<SettingsField>(); ClearEditors(); editors.Controls.Add(Title(T("Настройки не найдены", "No matching options"))); }
    }
    private void SelectGroup()
    {
        if (tree.SelectedNode?.Tag is not SettingsField[] fields)
        {
            var node = tree.SelectedNode; while (node?.Nodes.Count > 0 && node.Tag is not SettingsField[]) node = node.Nodes[0];
            if (node?.Tag is SettingsField[]) tree.SelectedNode = node; return;
        }
        selected = fields; ShowFields();
        if (!customSample)
        {
            var exampleField = presentation.Find(Model, search.Text).First(f => presentation.PageId(f) == presentation.PageId(fields[0]));
            loading = true; sample.Text = presentation.Example(exampleField); loading = false;
        }
        QueuePreview();
    }
    private void ClearEditors()
    { foreach (Control control in editors.Controls.Cast<Control>().ToArray()) if (control != heading) control.Dispose(); editors.Controls.Clear(); }
    private void ShowFields()
    {
        loading = true; editors.SuspendLayout(); ClearEditors();
        if (selected.Length == 0) { loading = false; editors.ResumeLayout(); return; }
        heading.Text = presentation.PageTitle(selected[0]); editors.Controls.Add(heading);
        var matchingSections = presentation.Find(Model, search.Text).Select(presentation.SectionId).Distinct().ToArray();
        foreach (var section in selected.GroupBy(presentation.SectionId).Where(g => matchingSections.Contains(g.Key)))
        {
            editors.Controls.Add(new Label { Text = presentation.SectionTitle(section.First()), AutoSize = true, MaximumSize = new Size(610, 0), Font = navigationGroupFont, Margin = new Padding(0, 16, 0, 10), AccessibleName = "Section:" + section.Key });
            foreach (var field in section)
            {
                string label = presentation.PageFieldLabel(field); object current = Model.Get(field.Id); Control editor;
                if (field.Kind == SettingsFieldKind.Boolean)
                {
                    var check = new CheckBox { Text = label, AutoSize = true, Checked = (bool)current, AccessibleName = field.Id, Margin = new Padding(0, 5, 0, 6) };
                    check.CheckedChanged += (_, _) => Change(field, check.Checked); editor = check;
                }
                else if (field.Kind == SettingsFieldKind.Integer)
                {
                    int maximum = field.Id == "indent.size" ? 10 : field.Id == "general.maxLineLength" ? 4096 : field.Member == "offset" ? Math.Min(100, field.Maximum) : field.Id.Contains("blankLines") ? 10 : Math.Min(4096, field.Maximum);
                    // Existing imported profiles remain intact until the user edits this value.
                    var number = new NumericUpDown { Minimum = field.Minimum, Maximum = Math.Max(maximum, (int)current), Value = (int)current, Width = 100, AccessibleName = field.Id };
                    number.ValueChanged += (_, _) =>
                    {
                        if (number.Value > maximum) { number.Value = maximum; return; }
                        Change(field, decimal.ToInt32(number.Value));
                    };
                    editor = FieldRow(label, number);
                }
                else
                {
                    var choice = new SettingsComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, AccessibleName = field.Id };
                    choice.Items.AddRange(field.Choices.Select(v => new Choice(v, presentation.ChoiceLabel(v))).Cast<object>().ToArray()); choice.SelectedIndex = field.Choices.ToList().IndexOf((string)current);
                    choice.SelectedIndexChanged += (_, _) => { if (choice.SelectedItem is Choice value) Change(field, value.Value); }; editor = FieldRow(label, choice);
                }
                editors.Controls.Add(editor);
            }
        }
        appearance.Apply(editors); editors.ResumeLayout(); loading = false;
    }
    private void Change(SettingsField field, object value)
    { if (!loading) Guard(() => { Model.Set(field.Id, value); dirty = true; QueuePreview(); }); }
    private void RefreshProfiles()
    {
        bool previous = loading; loading = true;
        foreach (var combo in new[] { profiles, generalProfiles })
        {
            combo.Items.Clear(); combo.Items.Add(new ProfileItem(null, T("Текущие настройки", "Current settings")));
            combo.Items.AddRange(Profiles.AvailableProfiles.Select(p => new ProfileItem(p, presentation.ProfileName(p))).Cast<object>().ToArray());
            combo.SelectedIndex = Math.Max(0, combo.Items.Cast<ProfileItem>().ToList().FindIndex(p => p.Profile?.Id == activeId));
        }
        var profile = Profiles.AvailableProfiles.FirstOrDefault(p => p.Id == activeId);
        profileName.Text = profile is null ? T("Мой профиль", "My profile") : presentation.ProfileName(profile); loading = previous;
    }
    private void SelectProfile(ComboBox combo)
    {
        if (loading || combo.SelectedItem is not ProfileItem item || item.Profile?.Id == activeId) return;
        Guard(() =>
        {
            PrepareSave(); activeId = item.Profile?.Id; Profiles.SetDefault(activeId);
            if (item.Profile is not null) Model = new SettingsEditorModel(item.Profile.Options);
            dirty = false; RefreshProfiles(); ShowFields(); QueuePreview();
        });
    }
    internal void PrepareSave()
    {
        if (dirty)
        {
            string name = profileName.Text.Trim();
            if (name.Length is 0 or > 100) throw new ArgumentException(T("Введите название профиля (1–100 символов).", "Enter a profile name (1–100 characters)."));
            bool replace = Profiles.Contains(name);
            if (replace && !string.Equals(activeId, "user:" + name, StringComparison.OrdinalIgnoreCase) && !Confirm(T("Заменить профиль «", "Replace profile “") + name + T("»?", "”?"))) throw new OperationCanceledException();
            Profiles.Save(name, Model.Options, replace); activeId = "user:" + name; dirty = false;
        }
        Profiles.SetDefault(activeId); activeId = Profiles.DefaultProfileId; RefreshProfiles();
    }
    internal bool Save()
    {
        try { PrepareSave(); SaveRequested?.Invoke(); return true; }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { ShowError(ex.Message); return false; }
    }
    private void Import() => Guard(() =>
    {
        using var dialog = new OpenFileDialog { Title = T("Импорт профиля", "Import profile"), Filter = "SQL Formatter JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var imported = new SqlFormatterProfileExchange().Import(dialog.FileName);
        if (!imported.Succeeded) throw new InvalidOperationException(imported.Diagnostics[0].Message);
        Model = new SettingsEditorModel(imported.Options!); activeId = null; Profiles.SetDefault(null); RefreshProfiles();
        profileName.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName); dirty = true; ShowFields(); QueuePreview();
    });
    private void Export() => Guard(() =>
    {
        using var dialog = new SaveFileDialog { Title = T("Экспорт профиля", "Export profile"), Filter = "SQL Formatter JSON (*.json)|*.json", DefaultExt = "json", FileName = profileName.Text + ".json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) new SqlFormatterProfileExchange().Export(dialog.FileName, Model.Options);
    });
    private void FormatSample() { InvalidatePreview(); RefreshPreview(); }
    private void InvalidatePreview() { revision++; previewCancellation?.Cancel(); debounce.Stop(); result.Clear(); error.Text = ""; }
    private void QueuePreview() { if (!loading && !IsDisposed) { InvalidatePreview(); debounce.Start(); } }
    private void RefreshPreview()
    {
        debounce.Stop(); if (!IsHandleCreated || IsDisposed) return;
        if (running) { debounce.Start(); return; }
        string sql = sample.Text; int currentRevision = revision; var options = Model.Options;
        var cancellation = previewCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2)); running = true;
        _ = Task.Run(() => SettingsPreview.Format(sql, options, null, cancellation.Token), cancellation.Token).ContinueWith(task =>
        {
            running = false; if (ReferenceEquals(previewCancellation, cancellation)) previewCancellation = null; cancellation.Dispose();
            if (IsDisposed || currentRevision != revision) return;
            result.Clear();
            if (task.IsCanceled) { error.Text = T("Превышено время форматирования примера.", "Example formatting timed out."); return; }
            if (task.IsFaulted) { error.Text = task.Exception?.GetBaseException().Message; return; }
            var diagnostic = task.Result.Diagnostics.FirstOrDefault(d => d.Severity == FormatterDiagnosticSeverity.Error);
            if (diagnostic is null && task.Result.Result is { } formatted) result.Text = formatted.Text;
            else error.Text = diagnostic is null ? T("Не удалось отформатировать пример.", "Unable to format example.") : diagnostic.Code + ": " + diagnostic.Message;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
    internal void StopPreview() { revision++; debounce.Stop(); previewCancellation?.Cancel(); }
    protected override void OnVisibleChanged(EventArgs e)
    { base.OnVisibleChanged(e); if (Visible) QueuePreview(); }
    private void Guard(Action action) { try { action(); } catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex.Message); } }
    private void ShowError(string message) => MessageBox.Show(this, message, "SQL Formatter", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private bool Confirm(string message) => MessageBox.Show(this, message, "SQL Formatter", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    private static Label Title(string text) => new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), Margin = new Padding(0, 0, 0, 18) };
    private static FlowLayoutPanel FieldRow(string label, Control input) => Row(new Label { Text = label, AutoSize = false, Width = 315, Height = 34, TextAlign = ContentAlignment.MiddleLeft }, input);
    private static FlowLayoutPanel Row(params Control[] controls)
    { var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) }; row.Controls.AddRange(controls); return row; }
    private static Button Button(string text, Action action)
    { var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(95, 30), Margin = new Padding(0, 6, 8, 6) }; button.Click += (_, _) => action(); return button; }
    private static Button Button(string text, Func<bool> action) => Button(text, () => { action(); });
    protected override void Dispose(bool disposing)
    { if (disposing) { StopPreview(); debounce.Dispose(); heading.Dispose(); } base.Dispose(disposing); if (disposing) navigationGroupFont.Dispose(); }
    private sealed class Choice
    { public Choice(string value, string label) { Value = value; Label = label; } public string Value { get; } public string Label { get; } public override string ToString() => Label; }
    private sealed class ProfileItem
    { public ProfileItem(FormattingProfile? profile, string label) { Profile = profile; Label = label; } public FormattingProfile? Profile { get; } public string Label { get; } public override string ToString() => Label; }
}
