using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class SettingsPresentationTests
{
    [Theory]
    [InlineData("VisualStudio")]
    [InlineData("Ssms")]
    public void Adapters_expose_two_commands_and_reuse_formatting_in_editor_context(string adapter)
    {
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Menus", adapter + ".vsct"));
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable";
        var menu = Assert.Single(document.Descendants(ns + "Menu"));
        Assert.Equal("menuSqlFormatter", (string?)menu.Attribute("id"));
        Assert.Equal("IDG_VS_MM_TOOLSADDINS", (string?)menu.Element(ns + "Parent")!.Attribute("id"));
        Assert.Equal("SQL Formatter", menu.Element(ns + "Strings")!.Element(ns + "ButtonText")!.Value);
        Assert.Contains(document.Descendants(ns + "Group"), g => (string?)g.Element(ns + "Parent")!.Attribute("id") == "IDM_VS_CTXT_CODEWIN");
        var buttons = document.Descendants(ns + "Button").ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.Contains(buttons, b => (string?)b.Attribute("id") == "cmdSettings");
        Assert.Contains(buttons, b => (string?)b.Attribute("id") == "cmdFormatDocument");
        Assert.Equal("cmdFormatDocument", (string?)Assert.Single(document.Descendants(ns + "CommandPlacement")).Attribute("id"));
    }

    [Fact]
    public void Navigation_groups_related_fields_and_keeps_all_settings_accessible()
    {
        var model = new SettingsEditorModel(); var presentation = new SettingsPresentation();
        var pages = model.Fields.GroupBy(presentation.PageId).ToArray();
        Assert.True(pages.Length < 150);
        Assert.Equal(model.Fields.Count, pages.Sum(g => g.Count()));
        Assert.Contains(pages.Single(p => p.Key == "general"), f => f.Id == "indent.size");
        Assert.Contains(pages.Single(p => p.Key == "general"), f => f.Id == "indent.style");
        Assert.Contains(pages.Single(p => p.Key == "execute.parameters"), f => f.Id == "rules.execute.parameters.stackList");
        foreach (var field in model.Fields) { Assert.NotEmpty(presentation.PageTitle(field)); Assert.NotEmpty(presentation.PageFieldLabel(field)); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_field_and_choice_has_a_human_label_and_a_category(bool russian)
    {
        var presentation = new SettingsPresentation(russian);
        var model = new SettingsEditorModel();
        foreach (var field in model.Fields)
        {
            Assert.NotEmpty(presentation.Path(field));
            Assert.NotEmpty(presentation.Title(field));
            Assert.NotEmpty(presentation.FieldLabel(field));
            Assert.DoesNotContain("rules.", presentation.Context(field));
            Assert.NotEmpty(presentation.Describe(model, field));
            foreach (string choice in field.Choices)
                Assert.NotEmpty(presentation.ChoiceLabel(choice));
        }
        foreach (var profile in new UserProfileStore().AvailableProfiles)
            Assert.NotEmpty(presentation.ProfileName(profile));
        Assert.Equal(5, new UserProfileStore().AvailableProfiles.Count);
    }

    [Fact]
    public void Search_uses_human_context_multiple_words_and_technical_aliases()
    {
        var model = new SettingsEditorModel();
        var presentation = new SettingsPresentation();
        Assert.Contains(presentation.Find(model, "EXEC вертикально"), f => f.RuleKey == "execute.parameters.stackList");
        Assert.Contains(presentation.Find(model, "СМЕЩЕНИЕ метки"), f => f.Id == "rules.labels.indent.offset");
        Assert.Single(presentation.Find(model, "rules.execute.parameters.stackList"));
        Assert.Empty(presentation.Find(model, "несуществующая настройка"));
        Assert.Equal(model.Fields.Count, presentation.Find(model, "").Count());
        var indent = model.Fields.Where(f => f.RuleKey == "execute.parameters.listIndent").ToArray();
        Assert.Equal(5, indent.Length);
        Assert.Single(indent.Select(presentation.GroupId).Distinct());
        Assert.Contains("наследование SELECT", presentation.Describe(model, model.Fields.First(f => f.Id == "rules.subquery.list.indent.offset")));
    }

    [Fact]
    public void Every_bundled_example_parses_and_produces_a_preview()
    {
        var presentation = new SettingsPresentation();
        var groups = new SettingsEditorModel().Fields.GroupBy(presentation.Example);
        foreach (var group in groups)
        {
            var preview = SettingsPreview.Format(group.Key, FormattingOptions.Default);
            Assert.NotNull(preview.Result);
            Assert.True(preview.Result.ParseSucceeded, group.First().Id + ": " + string.Join("; ", preview.Result.Diagnostics.Select(d => d.Message)));
            Assert.DoesNotContain(preview.Result.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error);
        }
    }

    [Theory]
    [InlineData("builtin:Compact")]
    [InlineData("native:ReadableVertical")]
    [InlineData("user:Рабочий")]
    public void Default_profile_survives_roundtrip_and_resolves_the_saved_snapshot(string id)
    {
        var store = new UserProfileStore();
        var model = new SettingsEditorModel();
        model.Set("rules.execute.parameters.stackList", "on");
        model.Set("alignment.setAssignments", true);
        store.Save("Рабочий", model.Options);
        store.SetDefault(id.ToUpperInvariant());
        Assert.Equal(id, store.DefaultProfileId);
        var restored = UserProfileStore.Deserialize(store.Serialize());
        Assert.Equal(id, restored.DefaultProfileId);
        Assert.Equal(new SettingsEditorModel(store.ResolveDefault(FormattingOptions.Default)).Export(),
            new SettingsEditorModel(restored.ResolveDefault(FormattingOptions.Default)).Export());
        restored.SetDefault(null);
        Assert.Same(FormattingOptions.Default, restored.ResolveDefault(FormattingOptions.Default));
        Assert.Equal(id, store.DefaultProfileId); // Cancel can discard the independently deserialized draft.
        Assert.IsType<JArray>(JToken.Parse(restored.Serialize()));
    }

    [Fact]
    public void Old_profile_arrays_and_names_colliding_with_builtin_ids_remain_supported()
    {
        var store = new UserProfileStore();
        store.Save("Compact", FormattingOptions.Default);
        string old = store.Serialize();
        Assert.IsType<JArray>(JToken.Parse(old));
        var restored = UserProfileStore.Deserialize(old);
        Assert.Null(restored.DefaultProfileId);
        Assert.Equal(2, restored.AvailableProfiles.Count(p => p.Name == "Compact"));
        restored.SetDefault("user:Compact");
        Assert.Same(restored.Get("Compact"), restored.ResolveDefault(FormattingOptions.Default));
        restored.Save("COMPACT", NativeFormattingPresets.Profiles[0].Options, replace: true);
        Assert.Same(restored.Get("Compact"), restored.ResolveDefault(FormattingOptions.Default));
    }

    [Fact]
    public void Invalid_default_references_and_malformed_envelopes_are_rejected()
    {
        var store = new UserProfileStore();
        store.SetDefault("builtin:Default");
        Assert.Throws<ArgumentException>(() => store.SetDefault("user:missing"));
        Assert.Equal("builtin:Default", store.DefaultProfileId);
        Assert.Throws<ArgumentException>(() => UserProfileStore.Deserialize("{\"version\":1,\"profiles\":[],\"defaultProfile\":\"user:missing\"}"));
        Assert.Throws<JsonSerializationException>(() => UserProfileStore.Deserialize("{\"version\":2,\"profiles\":[],\"defaultProfile\":\"builtin:Default\"}"));
        Assert.Throws<JsonSerializationException>(() => UserProfileStore.Deserialize("{\"version\":1,\"profiles\":[],\"defaultProfile\":null}"));
        Assert.Throws<JsonReaderException>(() => UserProfileStore.Deserialize("{\"version\":1,\"version\":1,\"profiles\":[],\"defaultProfile\":\"builtin:Default\"}"));
    }
}
