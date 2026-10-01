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
    private readonly CheckBox enabled = new() { Text = "Use full settings (instead of legacy pages)", AutoSize = true };
    private readonly ComboBox categories = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly TextBox search = new() { Width = 150, AccessibleName = "Search settings by key or scope" };
    private readonly ListBox fields = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly CheckBox boolean = new() { Text = "Enabled / true", AutoSize = true };
    private readonly ComboBox choice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly NumericUpDown number = new() { Width = 140 };
    private readonly Label details = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(640, 0) };
    private readonly ComboBox profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly TextBox profileName = new() { Width = 145, MaxLength = 100, AccessibleName = "New user profile name" };
    private readonly TextBox sample = SqlBox(false);
    private readonly TextBox result = SqlBox(true);
    private readonly CheckBox projectPreview = new() { Text = "Include project configuration (active SQL file)", AutoSize = true };
    private readonly TextBox source = new() { ReadOnly = true, Dock = DockStyle.Fill, Multiline = true, Height = 42, ScrollBars = ScrollBars.Vertical };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };
    private CancellationTokenSource? previewCancellation;
    private int revision;
    private bool running;
    private bool loading;

    internal FullSettingsControl(Func<string?> sqlPathProvider)
    {
        this.sqlPathProvider = sqlPathProvider;
        Model = new SettingsEditorModel();
        Profiles = new UserProfileStore();
        Size = new Size(480, 340);
        AutoScaleMode = AutoScaleMode.Font;
        AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(3) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(enabled, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var settingsTab = new TabPage("Settings");
        var profilesTab = new TabPage("Profiles");
        var previewTab = new TabPage("Preview");
        tabs.TabPages.AddRange(new[] { settingsTab, profilesTab, previewTab });
        layout.Controls.Add(tabs, 0, 1);
        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        settings.Controls.Add(Row(new Label { Text = "Category", AutoSize = true }, categories,
            new Label { Text = "Search", AutoSize = true }, search), 0, 0);
        settings.Controls.Add(fields, 0, 1);
        settings.Controls.Add(Row(boolean, choice, number, Button("Set value", SetValue), Button("Reset field", ResetField), Button("Reset all", ResetAll)), 0, 2);
        settings.Controls.Add(details, 0, 3);
        settings.SizeChanged += (_, _) => details.MaximumSize = new Size(Math.Max(150, settings.ClientSize.Width - 8), 0);
        settingsTab.Controls.Add(settings);
        var profileLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, WrapContents = false };
        profileLayout.Controls.Add(Row(profiles, Button("Load profile", LoadProfile)));
        profileLayout.Controls.Add(Row(profileName, Button("Save as…", SaveProfile)));
        profileLayout.Controls.Add(Row(Button("Import JSON…", Import), Button("Export JSON…", Export)));
        profileLayout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(360, 0),
            Text = "Named snapshots persist only after OK/Apply. Export writes a file immediately. Cancel discards unsaved settings/profile edits." });
        profilesTab.Controls.Add(profileLayout);
        var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        previews.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previews.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previews.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        previews.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previews.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        previews.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        previews.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previews.Controls.Add(projectPreview, 0, 0);
        previews.Controls.Add(new Label { Text = "Sample (not saved)", AutoSize = true }, 0, 1);
        previews.Controls.Add(sample, 0, 2);
        previews.Controls.Add(new Label { Text = "Core preview (not applied to editor)", AutoSize = true }, 0, 3);
        previews.Controls.Add(result, 0, 4);
        previews.Controls.Add(source, 0, 5);
        previews.Controls.Add(status, 0, 6);
        previews.SizeChanged += (_, _) => status.MaximumSize = new Size(Math.Max(150, previews.ClientSize.Width - 8), 0);
        previewTab.Controls.Add(previews);
        Controls.Add(layout);
        sample.MaxLength = 8192;
        sample.Text = "SELECT a, b FROM dbo.T WHERE a = 1 AND b = 2;";
        search.TextChanged += (_, _) => Filter();
        categories.SelectedIndexChanged += (_, _) => Filter();
        fields.SelectedIndexChanged += (_, _) => ShowField();
        sample.TextChanged += (_, _) => QueuePreview();
        projectPreview.CheckedChanged += (_, _) => QueuePreview();
        enabled.CheckedChanged += (_, _) => QueuePreview();
        debounce.Tick += (_, _) => RefreshPreview();
    }

    internal SettingsEditorModel Model { get; private set; }
    internal UserProfileStore Profiles { get; private set; }
    internal bool UseFullSettings => enabled.Checked;

    internal void LoadDraft(FormattingOptions options, UserProfileStore store, bool useFullSettings)
    {
        loading = true;
        Model = new SettingsEditorModel(options);
        Profiles = store;
        enabled.Checked = useFullSettings;
        search.Clear();
        categories.Items.Clear();
        categories.Items.Add("All");
        categories.Items.AddRange(Model.Fields.Select(f => f.Category).Distinct().OrderBy(c => c).Cast<object>().ToArray());
        categories.SelectedIndex = 0;
        RefreshProfiles();
        loading = false;
        Filter();
        QueuePreview();
    }

    private void Filter()
    {
        if (loading) return;
        string? selected = (fields.SelectedItem as SettingsField)?.Id;
        fields.BeginUpdate();
        fields.Items.Clear();
        fields.Items.AddRange(Model.Find(search.Text, categories.SelectedIndex <= 0 ? null : (string)categories.SelectedItem).Cast<object>().ToArray());
        fields.EndUpdate();
        int index = selected is null ? -1 : fields.Items.Cast<SettingsField>().ToList().FindIndex(f => f.Id == selected);
        fields.SelectedIndex = fields.Items.Count > 0 ? Math.Max(0, index) : -1;
        ShowField();
    }

    private void ShowField()
    {
        var f = fields.SelectedItem as SettingsField;
        boolean.Visible = f?.Kind == SettingsFieldKind.Boolean;
        number.Visible = f?.Kind == SettingsFieldKind.Integer;
        choice.Visible = f?.Kind == SettingsFieldKind.Choice;
        details.Text = f is null ? "No matching settings." : Model.Explain(f.Id);
        if (f is null) return;
        var value = Model.Get(f.Id);
        if (f.Kind == SettingsFieldKind.Boolean) boolean.Checked = (bool)value;
        if (f.Kind == SettingsFieldKind.Integer)
        {
            number.Minimum = int.MinValue;
            number.Maximum = int.MaxValue;
            number.Value = (int)value;
            number.Minimum = f.Minimum;
            number.Maximum = f.Maximum;
        }
        if (f.Kind == SettingsFieldKind.Choice)
        {
            choice.Items.Clear();
            choice.Items.AddRange(f.Choices.Cast<object>().ToArray());
            choice.SelectedItem = (string)value;
        }
    }

    private void SetValue() => Guard(() =>
    {
        if (fields.SelectedItem is not SettingsField f) return;
        Model.Set(f.Id, f.Kind switch { SettingsFieldKind.Boolean => (object)boolean.Checked, SettingsFieldKind.Integer => decimal.ToInt32(number.Value), _ => (string)choice.SelectedItem });
        enabled.Checked = true;
        ShowField();
        QueuePreview();
    });
    private void ResetField() => Guard(() =>
    {
        if (fields.SelectedItem is not SettingsField f) return;
        Model.Reset(f.Id);
        enabled.Checked = true;
        ShowField();
        QueuePreview();
    });
    private void ResetAll() => Guard(() =>
    {
        if (!Confirm("Reset all draft formatting values? Saved profiles are retained.")) return;
        Model.ResetAll();
        enabled.Checked = true;
        ShowField();
        QueuePreview();
    });

    private void RefreshProfiles()
    {
        profiles.Items.Clear();
        profiles.Items.AddRange(new FormattingProfileCatalog().Profiles.Select(p => "Built-in: " + p.Id).Cast<object>().ToArray());
        profiles.Items.AddRange(Profiles.Names.Select(n => "User: " + n).Cast<object>().ToArray());
        profiles.SelectedIndex = 0;
    }
    private void SaveProfile() => Guard(() =>
    {
        string name = profileName.Text.Trim();
        bool replace = Profiles.Contains(name);
        if (replace && !Confirm("Replace profile '" + name + "' in this draft?")) return;
        Profiles.Save(name, Model.Options, replace);
        RefreshProfiles();
        profiles.SelectedItem = "User: " + name;
        status.Text = "Profile saved in draft; choose OK/Apply to persist.";
    });
    private void LoadProfile() => Guard(() =>
    {
        if (profiles.SelectedItem is not string selection) return;
        FormattingOptions options;
        if (selection.StartsWith("User: ", StringComparison.Ordinal)) options = Profiles.Get(selection.Substring(6));
        else
        {
            new FormattingProfileCatalog().TryGet(selection.Substring(10), out var profile);
            options = profile!.Options;
        }
        Model = new SettingsEditorModel(options);
        enabled.Checked = true;
        ShowField();
        QueuePreview();
    });
    private void Import() => Guard(() =>
    {
        using var dialog = new OpenFileDialog { Filter = "Native formatter JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var imported = new SqlFormatterProfileExchange().Import(dialog.FileName);
        if (!imported.Succeeded) throw new InvalidOperationException(imported.Diagnostics[0].Message);
        Model = new SettingsEditorModel(imported.Options!);
        enabled.Checked = true;
        ShowField();
        QueuePreview();
    });
    private void Export() => Guard(() =>
    {
        using var dialog = new SaveFileDialog { Filter = "Native formatter JSON (*.json)|*.json", DefaultExt = "json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) new SqlFormatterProfileExchange().Export(dialog.FileName, Model.Options);
    });

    private void QueuePreview()
    {
        if (loading || IsDisposed) return;
        revision++;
        previewCancellation?.Cancel();
        debounce.Stop();
        debounce.Start();
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
        {
            source.Text = "No active saved .sql file; project preview unavailable.";
            result.Clear();
            return;
        }
        int currentRevision = revision;
        var options = Model.Options;
        var cancellation = previewCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        running = true;
        status.Text = "Previewing draft; defaults change only after OK/Apply.";
        _ = Task.Run(() => SettingsPreview.Format(sql, options, path, cancellation.Token), cancellation.Token).ContinueWith(task =>
        {
            running = false;
            if (ReferenceEquals(previewCancellation, cancellation)) previewCancellation = null;
            cancellation.Dispose();
            if (IsDisposed || currentRevision != revision) return;
            result.Clear();
            if (task.IsCanceled) { status.Text = "Preview cancelled or exceeded 2 seconds."; return; }
            if (task.IsFaulted) { status.Text = "Preview failed: " + task.Exception?.GetBaseException().Message; return; }
            source.Text = task.Result.Source;
            var error = task.Result.Diagnostics.FirstOrDefault();
            // Native multiline EDIT controls require CRLF. This is display-only;
            // the formatter's configured line endings and editor edits are unchanged.
            if (error is null) result.Text = task.Result.Result!.Text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            status.Text = error is null ? (enabled.Checked ? "Draft preview ready." : "Draft preview ready; full settings disabled for commands until enabled.") : error.Code + ": " + error.Message;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
    internal void StopPreview()
    {
        revision++;
        debounce.Stop();
        previewCancellation?.Cancel();
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "T-SQL Formatter", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private bool Confirm(string message) => MessageBox.Show(this, message, "T-SQL Formatter", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        row.Controls.AddRange(controls);
        return row;
    }
    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }
    private static TextBox SqlBox(bool readOnly) => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = readOnly, WordWrap = false,
        ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 9)
    };
    protected override void Dispose(bool disposing)
    {
        if (disposing) { StopPreview(); debounce.Dispose(); }
        base.Dispose(disposing);
    }
}
