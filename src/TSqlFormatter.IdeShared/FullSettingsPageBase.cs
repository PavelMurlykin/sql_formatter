using System;
using System.ComponentModel;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.PlatformUI;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.IdeShared;

/// <summary>Explicit Save/Apply commits a draft; closing discards edits made since the last save.</summary>
public abstract class FullSettingsPageBase : DialogPage
{
    private FullSettingsControl? control;
    private bool opened;
    [Browsable(false)] public bool UseFullSettings { get; set; }
    [Browsable(false)] public string ConfigurationJson { get; set; } = "";
    [Browsable(false)] public string ProfilesJson { get; set; } = "";
    internal Func<FormattingOptions>? LegacyOptionsProvider { get; set; }
    internal Func<string?>? SqlPathProvider { get; set; }
    internal Func<string>? ShortcutProvider { get; set; }
    internal Action<string>? ShortcutApply { get; set; }

    public FormattingOptions ResolveOptions(FormattingOptions legacy) => UseFullSettings
        ? StoredDraft(legacy) : legacy;

    private FormattingOptions StoredDraft(FormattingOptions fallback) => UserProfileStore.Deserialize(ProfilesJson)
        .ResolveDefault(string.IsNullOrWhiteSpace(ConfigurationJson) ? fallback
            : new SqlFormatterConfigurationSerializer().Deserialize(ConfigurationJson));

    public void ImportOptions(FormattingOptions options)
    {
        ConfigurationJson = new SqlFormatterConfigurationSerializer().SerializeV2(options);
        var profiles = UserProfileStore.Deserialize(ProfilesJson);
        profiles.SetDefault(null);
        ProfilesJson = profiles.Serialize();
        UseFullSettings = true;
    }

    internal void ShowEditor(bool showProfiles, IntPtr owner)
    {
        string? sqlPath = SqlPathProvider?.Invoke();
        using var editor = new FullSettingsControl(() => sqlPath);
        editor.LoadDraft(StoredDraft(LegacyOptionsProvider?.Invoke() ?? FormattingOptions.Default),
            UserProfileStore.Deserialize(ProfilesJson), UseFullSettings);
        editor.Shortcut = ShortcutProvider?.Invoke() ?? "";
        editor.SaveRequested += () => SaveEditor(editor);
        if (showProfiles) editor.ShowProfiles();
        using var dialog = new FormattingSettingsWindow(editor);
        void ThemeChanged(ThemeChangedEventArgs _) { if (!dialog.IsDisposed) { var theme = IdeHostIntegration.Appearance(); theme.Apply(dialog); editor.ApplyAppearance(theme); } }
        ThemeChanged(null!);
        dialog.HandleCreated += (_, _) => IdeHostIntegration.Appearance().ApplyCaption(dialog);
        VSColorTheme.ThemeChanged += ThemeChanged;
        try { dialog.ShowDialog(new DialogOwner(owner)); }
        finally { VSColorTheme.ThemeChanged -= ThemeChanged; }
    }

    private void SaveEditor(FullSettingsControl editor)
    {
        ShortcutApply?.Invoke(editor.Shortcut);
        ConfigurationJson = editor.Model.Export();
        ProfilesJson = editor.Profiles.Serialize();
        UseFullSettings = editor.UseFullSettings;
        SaveSettingsToStorage();
    }

    private sealed class DialogOwner : IWin32Window
    {
        internal DialogOwner(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }

    protected override IWin32Window Window => control ??= new FullSettingsControl(() => SqlPathProvider?.Invoke());
    protected override void OnActivate(CancelEventArgs e)
    {
        base.OnActivate(e);
        if (opened) return;
        _ = Window;
        try
        {
            control!.LoadDraft(StoredDraft(LegacyOptionsProvider?.Invoke() ?? FormattingOptions.Default),
                UserProfileStore.Deserialize(ProfilesJson), UseFullSettings);
            control.Shortcut = ShortcutProvider?.Invoke() ?? "";
            control.ApplyAppearance(IdeHostIntegration.Appearance());
            control.SaveRequested -= SaveControl;
            control.SaveRequested += SaveControl;
            opened = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Cannot load formatter settings: " + ex.Message, "T-SQL Formatter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            e.Cancel = true;
        }
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        if (control is not null && opened)
        {
            try { control.PrepareSave(); ShortcutApply?.Invoke(control.Shortcut); }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException) MessageBox.Show(ex.Message, "SQL Formatter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.ApplyBehavior = ApplyKind.Cancel; return;
            }
            ConfigurationJson = control.Model.Export();
            ProfilesJson = control.Profiles.Serialize();
            UseFullSettings = control.UseFullSettings;
        }
        base.OnApply(e);
    }

    private void SaveControl() { if (control is not null) SaveEditor(control); }

    protected override void OnClosed(EventArgs e)
    {
        opened = false;
        control?.StopPreview();
        base.OnClosed(e);
    }
}
