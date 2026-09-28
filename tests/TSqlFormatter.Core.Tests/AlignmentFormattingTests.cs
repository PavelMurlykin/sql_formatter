using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class AlignmentFormattingTests
{
    private readonly ScriptDomSqlFormatter formatter = new();

    [Fact]
    public void Aligns_explicit_select_aliases()
    {
        const string source = "select Id as CustomerId,LongName as Name from T";
        var options = new FormattingOptions(alignment: new AlignmentOptions(selectAliases: true));

        var result = formatter.Format(source, options, new FormatRequest());

        Assert.Equal("SELECT\n    Id       AS CustomerId,\n    LongName AS Name\nFROM T", result.Text);
        Assert.Equal(result.Text, formatter.Format(result.Text, options, new FormatRequest()).Text);
    }

    [Fact]
    public void Aligns_update_set_assignments()
    {
        const string source = "update T set Id=1,LongName=2 where Id=3";
        var options = new FormattingOptions(alignment: new AlignmentOptions(setAssignments: true));

        var result = formatter.Format(source, options, new FormatRequest());

        Assert.Equal("UPDATE T\nSET\n    Id       = 1,\n    LongName = 2\nWHERE\n    Id = 3", result.Text);
        Assert.Equal(result.Text, formatter.Format(result.Text, options, new FormatRequest()).Text);
    }

    [Fact]
    public void Aligns_declare_types()
    {
        const string source = "declare @a int,@long bigint;";
        var options = new FormattingOptions(alignment: new AlignmentOptions(declareTypes: true));

        var result = formatter.Format(source, options, new FormatRequest());

        Assert.Equal("DECLARE\n    @a    int,\n    @long bigint;", result.Text);
        Assert.Equal(result.Text, formatter.Format(result.Text, options, new FormatRequest()).Text);
    }

    [Fact]
    public void Does_not_align_when_result_would_exceed_width()
    {
        const string source = "select A as B,LongName as LongAlias from T";
        var options = new FormattingOptions(
            general: new GeneralOptions(maxLineWidth: 20),
            alignment: new AlignmentOptions(selectAliases: true));

        var result = formatter.Format(source, options, new FormatRequest());

        Assert.DoesNotContain("A        AS B", result.Text);
        Assert.True(result.ParseSucceeded);
    }

    [Fact]
    public void Alignment_json_is_opt_in_and_round_trips()
    {
        var serializer = new SqlFormatterConfigurationSerializer();
        var parsed = serializer.Parse("""
            {"version":1,"alignment":{"selectAliases":true,"setAssignments":true,"declareTypes":true}}
            """);

        Assert.True(parsed.Succeeded);
        Assert.True(parsed.Options!.Alignment.SelectAliases);
        Assert.True(parsed.Options.Alignment.SetAssignments);
        Assert.True(parsed.Options.Alignment.DeclareTypes);
        Assert.True(serializer.Deserialize(serializer.Serialize(parsed.Options)).Alignment.DeclareTypes);
        Assert.False(FormattingOptions.Default.Alignment.SelectAliases);
        Assert.False(serializer.Parse("""{"version":1,"alignment":{"selectAliases":"yes"}}""").Succeeded);
    }
}
