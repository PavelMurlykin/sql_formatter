using System;
using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Ssms;

public enum SsmsProfileId
{
    Default,
    Compact,
    Expanded
}

/// <summary>Per-user SSMS defaults; a nearby .tsqlformatter.json overrides them.</summary>
public sealed class SsmsOptionsPage : DialogPage
{
    private int maxLineLength = 100;
    private int indentSize = 4;

    [Category("Profile"), DisplayName("Built-in profile")]
    [Description("Default uses the fields below. Compact and Expanded use their built-in settings; a nearby JSON config can override either.")]
    [DefaultValue(SsmsProfileId.Default)]
    public SsmsProfileId Profile { get; set; } = SsmsProfileId.Default;

    [Category("General"), DisplayName("Maximum line length")]
    [Description("Preferred maximum width of a formatted SQL line (1–4096). Used by the Default profile.")]
    [DefaultValue(100)]
    public int MaxLineLength
    {
        get => maxLineLength;
        set => maxLineLength = value >= 1 && value <= 4096
            ? value : throw new ArgumentOutOfRangeException(nameof(value), "Use a value from 1 to 4096.");
    }

    [Category("General"), DisplayName("Line ending")]
    [DefaultValue(DocLineEnding.Lf)]
    public DocLineEnding LineEnding { get; set; } = DocLineEnding.Lf;

    [Category("General"), DisplayName("Final newline")]
    [DefaultValue(false)]
    public bool FinalNewLine { get; set; }

    [Category("Indent"), DisplayName("Indent size")]
    [Description("Indent width (0–32). Used by the Default profile.")]
    [DefaultValue(4)]
    public int IndentSize
    {
        get => indentSize;
        set => indentSize = value >= 0 && value <= 32
            ? value : throw new ArgumentOutOfRangeException(nameof(value), "Use a value from 0 to 32.");
    }

    [Category("Indent"), DisplayName("Use tabs")]
    [DefaultValue(false)]
    public bool UseTabs { get; set; }

    [Category("Keywords"), DisplayName("Keyword casing")]
    [DefaultValue(KeywordCase.Upper)]
    public KeywordCase KeywordCase { get; set; } = KeywordCase.Upper;

    public FormattingOptions CreateOptions()
    {
        if (Profile != SsmsProfileId.Default)
        {
            if (!new FormattingProfileCatalog().TryGet(Profile.ToString(), out var profile) || profile == null)
                throw new InvalidOperationException($"Unknown SSMS formatting profile '{Profile}'.");
            return profile.Options;
        }

        return FormattingOptions.Default.With(
            general: new GeneralOptions(MaxLineLength, LineEnding, FinalNewLine),
            indent: new IndentOptions(IndentSize, UseTabs),
            keywords: new KeywordOptions(KeywordCase));
    }
}
