using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class ConfigurationProfileTests
{
    [Fact]
    public void Built_in_profiles_are_named_and_distinct()
    {
        var catalog = new FormattingProfileCatalog();

        Assert.Equal(new[] { "Default", "Compact", "Expanded" },
            catalog.Profiles.Select(profile => profile.Id));
        Assert.True(catalog.TryGet("compact", out var compact));
        Assert.Equal(120, compact!.Options.General.MaxLineWidth);
        Assert.True(catalog.TryGet("EXPANDED", out var expanded));
        Assert.Equal(80, expanded!.Options.General.MaxLineWidth);
        Assert.Equal(SelectColumnLayout.OnePerLine, expanded.Options.Select.ColumnLayout);
        Assert.Equal(ClauseItemLayout.OnePerLine, expanded.Options.Clauses.OrderByLayout);
    }

    [Fact]
    public void File_and_explicit_options_layer_over_selected_profile()
    {
        var result = new SqlFormatterConfigurationResolver().Resolve(
            """{"version":1,"keywords":{"case":"lower"}}""",
            new FormattingOptionsOverrides(maxLineLength: 90),
            profileId: "Expanded");

        Assert.True(result.Succeeded);
        Assert.Equal(90, result.Options!.General.MaxLineWidth);
        Assert.Equal(KeywordCase.Lower, result.Options.Keywords.Case);
        Assert.Equal(SelectColumnLayout.OnePerLine, result.Options.Select.ColumnLayout);
        Assert.Equal(ClauseItemLayout.OnePerLine, result.Options.Clauses.GroupByLayout);
    }

    [Fact]
    public void Custom_project_profile_can_be_selected()
    {
        var project = new FormattingProfile("project", "Project", FormattingOptions.Default.With(
            indent: new IndentOptions(size: 2, useTabs: true)));
        var catalog = new FormattingProfileCatalog(new[] { project });
        var resolver = new SqlFormatterConfigurationResolver(catalog);
        var result = resolver.Resolve("""{"version":1,"general":{"maxLineLength":110}}""",
            profileId: "PROJECT");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Options!.Indent.Size);
        Assert.True(result.Options.Indent.UseTabs);
        Assert.Equal(110, result.Options.General.MaxLineWidth);
    }

    [Fact]
    public void Unknown_profile_returns_diagnostic_without_options()
    {
        var result = new SqlFormatterConfigurationResolver().Resolve(profileId: "missing");

        Assert.False(result.Succeeded);
        Assert.Null(result.Options);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TSF2000", diagnostic.Code);
        Assert.Contains("missing", diagnostic.Message);
    }

    [Fact]
    public void Duplicate_profile_id_is_rejected_case_insensitively()
    {
        var profile = new FormattingProfile("default", "Collision", FormattingOptions.Default);

        Assert.Throws<ArgumentException>(() => new FormattingProfileCatalog(new[] { profile }));
    }
}
