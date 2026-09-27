using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace TSqlFormatter.VisualStudio;

public enum IdeProfileId
{
    Default,
    Compact,
    Expanded
}

/// <summary>Selects a built-in formatting profile for the VSIX.</summary>
public sealed class ProfileOptionsPage : DialogPage
{
    [Category("Profile"), DisplayName("Built-in profile")]
    [Description("Default uses the other IDE option pages. Compact and Expanded use their built-in settings; a project config can override them.")]
    [DefaultValue(IdeProfileId.Default)]
    public IdeProfileId Profile { get; set; } = IdeProfileId.Default;
}
