using Newtonsoft.Json.Linq;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class SettingsEditorTests
{
    [Fact]
    public void Every_catalog_scalar_has_a_searchable_typed_editor_and_accepts_a_distinct_value()
    {
        var model = new SettingsEditorModel();
        Assert.Equal(16, model.Fields.Count(f => f.RuleKey is null));
        foreach (var d in RuleCatalog.Default.Definitions.Values)
        {
            int count = d.DefaultValue.Kind switch { RuleValueKind.Indent => 5, RuleValueKind.Threshold => 2, _ => 1 };
            Assert.Equal(count, model.Fields.Count(f => f.RuleKey == d.Key));
        }
        Assert.Equal(model.Fields.Count, model.Fields.Select(f => f.Id).Distinct().Count());
        foreach (var f in model.Fields)
        {
            Assert.Contains(f, model.Find(f.Id.ToUpperInvariant(), f.Category));
            var before = model.Get(f.Id);
            object after = f.Kind switch
            {
                SettingsFieldKind.Boolean => !(bool)before,
                SettingsFieldKind.Integer => (int)before < f.Maximum ? (int)before + 1 : (int)before - 1,
                _ => f.Choices.First(v => v != (string)before)
            };
            model.Set(f.Id, after);
            Assert.Equal(after, model.Get(f.Id));
            Assert.Equal(after, new SettingsEditorModel(new SqlFormatterConfigurationSerializer().Deserialize(model.Export())).Get(f.Id));
            model.Reset(f.Id);
            Assert.Equal(before, model.Get(f.Id));
        }
        Assert.Empty(model.Options.Rules.Overrides);
    }

    [Fact]
    public void Every_applicable_ledger_mapping_is_present_in_the_editor()
    {
        var fields = new SettingsEditorModel().Fields;
        var rows = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", "coverage.tsv"))
            .Skip(1).Select(l => l.Split('\t')).Where(r => r[2] == "covered");
        foreach (var row in rows)
        {
            string mapping = row[3].Split(':')[0];
            Assert.True(fields.Any(f => f.RuleKey is { } key && (key == mapping || mapping.StartsWith(key + ".", StringComparison.Ordinal))
                || f.Id == mapping), row[0] + " → " + mapping);
        }
    }

    [Fact]
    public void Invalid_edits_and_imports_are_atomic_and_reset_preserves_other_members()
    {
        var model = new SettingsEditorModel();
        model.Set("rules.labels.indent.offset", 2);
        model.Set("rules.labels.indent.enabled", true);
        model.Reset("rules.labels.indent.offset");
        Assert.True((bool)model.Get("rules.labels.indent.enabled"));
        Assert.Equal(0, model.Get("rules.labels.indent.offset"));
        string before = model.Export();
        Assert.Throws<ArgumentException>(() => model.Set("rules.labels.indent.offset", 33));
        Assert.Throws<ArgumentException>(() => model.Set("rules.labels.indent.style", "2"));
        Assert.Throws<ArgumentException>(() => model.Set("general.maxLineLength", 0));
        Assert.Throws<ArgumentException>(() => model.Set("rules.labels.indent.enabled", "true"));
        Assert.Throws<KeyNotFoundException>(() => model.Get("unknown"));
        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => model.Import("{\"version\":2,\"rules\":{\"unknown\":true}}"));
        Assert.Equal(before, model.Export());
        model.ResetAll();
        Assert.Equal(new SettingsEditorModel().Export(), model.Export());
    }

    [Fact]
    public void Dependencies_are_explained_and_inactive_values_retained()
    {
        var model = new SettingsEditorModel();
        model.Set("rules.execute.parameters.stackMode", "auto");
        Assert.Contains("execute.parameters.stackList = inherit", model.Explain("rules.execute.parameters.stackMode"));
        model.Set("rules.execute.parameters.stackList", "off");
        Assert.Contains("= off", model.Explain("rules.execute.parameters.stackMode"));
        Assert.Equal("auto", model.Get("rules.execute.parameters.stackMode"));
        Assert.Contains(".enabled = False", model.Explain("rules.labels.indent.offset"));
        Assert.Contains("useSelectFormatting = false", model.Explain("rules.subquery.list.indent.offset"));
    }

    [Fact]
    public void Native_profile_exchange_and_named_snapshots_do_not_drop_rules_or_alignment()
    {
        var model = new SettingsEditorModel();
        model.Set("rules.execute.parameters.stackList", "on");
        model.Set("alignment.setAssignments", true);
        var profiles = new UserProfileStore();
        profiles.Save("Рабочий профиль", model.Options);
        model.ResetAll();
        Assert.True(profiles.Get("рабочий профиль").Alignment.SetAssignments);
        Assert.Equal("on", profiles.Get("Рабочий профиль").Rules.Get("execute.parameters.stackList").Choice);
        var copy = UserProfileStore.Deserialize(profiles.Serialize());
        Assert.Equal(profiles.Names, copy.Names);
        Assert.Equal(new SettingsEditorModel(profiles.Get(copy.Names[0])).Export(), new SettingsEditorModel(copy.Get(copy.Names[0])).Export());
        Assert.Throws<ArgumentException>(() => copy.Save("РАБОЧИЙ ПРОФИЛЬ", model.Options));
        copy.Save("Рабочий профиль", model.Options, replace: true);
        Assert.Empty(copy.Get(copy.Names[0]).Rules.Overrides);
        Assert.Throws<ArgumentException>(() => copy.Save("  ", model.Options));
        Assert.Throws<ArgumentException>(() => UserProfileStore.Deserialize("[{\"name\":\"x\",\"configuration\":{\"version\":1}},{\"name\":\"X\",\"configuration\":{\"version\":1}}]"));
        var path = Path.Combine(Path.GetTempPath(), "tsql-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var exchange = new SqlFormatterProfileExchange();
            exchange.Export(path, profiles.Get(profiles.Names[0]));
            var imported = exchange.Import(path);
            Assert.True(imported.Succeeded);
            Assert.True(imported.Options!.Alignment.SetAssignments);
            Assert.Equal("on", imported.Options.Rules.Get("execute.parameters.stackList").Choice);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Draft_preview_core_and_cli_use_identical_project_precedence_and_native_configuration()
    {
        const string sql = "EXEC dbo.p @a = 1, @b = N'keep';";
        var model = new SettingsEditorModel();
        model.Set("rules.execute.parameters.stackList", "on");
        model.Set("rules.execute.parameters.listIndent.enabled", true);
        model.Set("rules.execute.parameters.listIndent.offset", 1);
        var standalone = SettingsPreview.Format(sql, model.Options);
        Assert.Equal(new ScriptDomSqlFormatter().Format(sql, model.Options, new FormatRequest()).Text, standalone.Result!.Text);
        Assert.Contains("project configuration excluded", standalone.Source);
        Assert.Throws<ArgumentException>(() => SettingsPreview.Format(new string(' ', 8193), model.Options));
        var directory = Path.Combine(Path.GetTempPath(), "tsql-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "query.sql");
            File.WriteAllText(path, sql);
            string editorConfig = Path.Combine(directory, ".editorconfig");
            File.WriteAllText(editorConfig, "root = true\n[*.sql]\nindent_size = 2\n");
            string json = Path.Combine(directory, ".tsqlformatter.json");
            File.WriteAllText(json, model.Export());
            var preview = SettingsPreview.Format(sql, FormattingOptions.Default, path);
            Assert.Contains(editorConfig, preview.Source);
            Assert.Contains(json, preview.Source);
            Assert.True(preview.Source.IndexOf(editorConfig, StringComparison.Ordinal) < preview.Source.IndexOf(json, StringComparison.Ordinal));
            var resolved = new SqlFormatterConfigurationResolver().ResolveForSqlFile(path, FormattingOptions.Default);
            Assert.Equal(4, resolved.Options!.Indent.Size); // JSON wins over EditorConfig.
            Assert.Equal(new ScriptDomSqlFormatter().Format(sql, resolved.Options, new FormatRequest()).Text, preview.Result!.Text);
            using var input = new StringReader("");
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await TSqlFormatter.Cli.SqlFormatterCli.RunAsync(new[] { path }, input, output, error));
            Assert.Equal(preview.Result.Text, output.ToString());
            Assert.Empty(error.ToString());
            File.WriteAllText(json, "{\"version\":2,\"rules\":{\"unknown\":true}}");
            var invalid = SettingsPreview.Format(sql, model.Options, path);
            Assert.Null(invalid.Result);
            Assert.Contains(invalid.Diagnostics, d => d.Code == "TSF2000");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Default_editor_does_not_materialize_opt_in_rules_or_change_v1_output()
    {
        var model = new SettingsEditorModel();
        Assert.Empty(model.Options.Rules.Overrides);
        Assert.Equal(2, JObject.Parse(model.Export())["version"]);
        var v1 = new SqlFormatterConfigurationSerializer().Deserialize("{\"version\":1,\"keywords\":{\"case\":\"lower\"}}");
        model.Import(new SqlFormatterConfigurationSerializer().Serialize(v1));
        Assert.Empty(model.Options.Rules.Overrides);
        const string sql = "BEGIN TRY SELECT a, b FROM T; END TRY BEGIN CATCH PRINT 1; END CATCH;";
        Assert.Equal(new ScriptDomSqlFormatter().Format(sql, v1, new FormatRequest()).Text,
            SettingsPreview.Format(sql, model.Options).Result!.Text);
    }
}
