using System;
using System.ComponentModel;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.IdeShared;

/// <summary>Persist only on Apply/OK. Preview and Cancel never change the formatting defaults.</summary>
public abstract class FullSettingsPageBase : DialogPage
{
    private FullSettingsControl? control;
    private bool opened;
    [Browsable(false)] public bool UseFullSettings { get; set; }
    [Browsable(false)] public string ConfigurationJson { get; set; } = "";
    [Browsable(false)] public string ProfilesJson { get; set; } = "";
    internal Func<FormattingOptions>? LegacyOptionsProvider { get; set; }
    internal Func<string?>? SqlPathProvider { get; set; }

    public FormattingOptions ResolveOptions(FormattingOptions legacy) => UseFullSettings && !string.IsNullOrWhiteSpace(ConfigurationJson)
        ? new SqlFormatterConfigurationSerializer().Deserialize(ConfigurationJson) : legacy;

    public void ImportOptions(FormattingOptions options)
    {
        ConfigurationJson = new SqlFormatterConfigurationSerializer().SerializeV2(options);
        UseFullSettings = true;
    }

    protected override IWin32Window Window => control ??= new FullSettingsControl(() => SqlPathProvider?.Invoke());
    protected override void OnActivate(CancelEventArgs e)
    {
        base.OnActivate(e);
        if (opened) return;
        _ = Window;
        try
        {
            control!.LoadDraft(ResolveOptions(LegacyOptionsProvider?.Invoke() ?? FormattingOptions.Default),
                UserProfileStore.Deserialize(ProfilesJson), UseFullSettings);
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
            ConfigurationJson = control.Model.Export();
            ProfilesJson = control.Profiles.Serialize();
            UseFullSettings = control.UseFullSettings;
        }
        base.OnApply(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        opened = false;
        control?.StopPreview();
        base.OnClosed(e);
    }
}
