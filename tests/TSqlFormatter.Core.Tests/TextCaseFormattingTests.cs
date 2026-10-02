using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class TextCaseFormattingTests
{
    private static string Format(string source, string rules)
    {
        var options = new SqlFormatterConfigurationSerializer().Deserialize(
            "{\"version\":2,\"rules\":{" + rules + "}}");
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(source, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        var second = formatter.Format(first.Text, options, new FormatRequest());
        Assert.Equal(first.Text, second.Text);
        return first.Text;
    }

    [Fact]
    public void Keyword_case_can_override_version_one_keyword_setting()
    {
        Assert.Equal("select 1", Format("SELECT 1", "\"textCase.keyword\":\"lower\""));
        Assert.Equal("SELECT 1", Format("select 1", "\"textCase.keyword\":\"upper\""));
    }

    [Fact]
    public void Builtin_function_case_does_not_change_schema_qualified_user_function()
    {
        var text = Format("select abs(-1), dbo.myFunc(2) from dbo.T",
            "\"textCase.builtin\":\"upper\"");
        Assert.Contains("ABS(-1)", text);
        Assert.Contains("dbo.myFunc(2)", text);
    }

    [Fact]
    public void Data_type_case_is_independent_of_keyword_case()
    {
        var text = Format("DECLARE @x INT;", "\"textCase.dataType\":\"lower\"");
        Assert.Contains("DECLARE @x int", text);
    }

    [Fact]
    public void Identifier_and_variable_case_are_independent()
    {
        var text = Format("select customerId, @myVar from dbo.CustomerTable",
            "\"textCase.identifier\":\"upper\",\"textCase.variable\":\"lower\"");
        Assert.Contains("CUSTOMERID", text);
        Assert.Contains("@myvar", text);
        Assert.Contains("DBO.CUSTOMERTABLE", text);
    }

    [Fact]
    public void Alias_case_only_changes_explicit_aliases()
    {
        var text = Format("select Id as displayName from dbo.T shortName",
            "\"textCase.alias\":\"upper\"");
        Assert.Contains("Id AS DISPLAYNAME", text);
        Assert.Contains("dbo.T SHORTNAME", text);
    }

    [Fact]
    public void Quoted_identifier_switch_controls_casing_without_touching_literals_or_comments()
    {
        const string source = "SELECT [mixedName], 'mixedName' FROM [myTable] -- mixedName";
        var without = Format(source, "\"textCase.identifier\":\"upper\"");
        var with = Format(source,
            "\"textCase.identifier\":\"upper\",\"textCase.formatQuotedIdentifier\":true");

        Assert.Contains("[mixedName]", without);
        Assert.Contains("[MIXEDNAME]", with);
        Assert.Contains("[MYTABLE]", with);
        Assert.Contains("'mixedName'", with);
        Assert.Contains("-- mixedName", with);
    }

    [Fact]
    public void All_eight_text_case_paths_are_registered()
    {
        var keys = RuleCatalog.Default.Definitions.Keys
            .Where(key => key.StartsWith("textCase.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(8, keys.Length);
    }

    [Fact]
    public void Custom_rule_catalog_without_text_case_keys_keeps_legacy_formatting()
    {
        var rules = new RuleOptions(new RuleCatalog(Array.Empty<RuleDescriptor>()));
        var options = FormattingOptions.Default.With(rules: rules);
        var result = new ScriptDomSqlFormatter().Format("select mixedName", options, new FormatRequest());
        Assert.Equal("SELECT mixedName", result.Text);
    }
}
