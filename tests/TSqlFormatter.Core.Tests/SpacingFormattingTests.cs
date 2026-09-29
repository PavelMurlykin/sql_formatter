using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class SpacingFormattingTests
{
    private static string Format(string sql, string rules)
    {
        var options = new SqlFormatterConfigurationSerializer().Deserialize(
            "{\"version\":2,\"rules\":{" + rules + "}}");
        var formatter = new ScriptDomSqlFormatter();
        var first = formatter.Format(sql, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        return first.Text;
    }

    [Theory]
    [InlineData("spacing.beforeComma", "SELECT 1,2", "1 ,")]
    [InlineData("spacing.afterComma", "SELECT 1,2", ", 2")]
    [InlineData("spacing.beforeDot", "SELECT t.Col FROM dbo.T AS t", "t .")]
    [InlineData("spacing.afterDot", "SELECT t.Col FROM dbo.T AS t", ". Col")]
    [InlineData("spacing.beforeScopeResolution", "SELECT geometry::STGeomFromText('POINT (1 1)', 0)", "geometry ::")]
    [InlineData("spacing.afterScopeResolution", "SELECT geometry::STGeomFromText('POINT (1 1)', 0)", ":: STGeomFromText")]
    [InlineData("spacing.beforeFunctionArguments", "SELECT ABS(1)", "ABS (")]
    [InlineData("spacing.withinEmptyFunctionArguments", "SELECT GETDATE()", "GETDATE( )")]
    [InlineData("spacing.withinFunctionArguments", "SELECT ABS(1)", "ABS( 1 )")]
    [InlineData("spacing.arithmeticOperators", "SELECT 1+2", "1 + 2")]
    public void Insert_rule_changes_only_requested_boundary(string key, string source, string expected)
    {
        var text = Format(source, "\"" + key + "\":\"insert\"");
        Assert.Contains(expected, text);
    }

    [Theory]
    [InlineData("spacing.beforeComma", "SELECT 1 , 2", "1,")]
    [InlineData("spacing.afterComma", "SELECT 1, 2", ",2")]
    [InlineData("spacing.beforeDot", "SELECT t . Col FROM dbo.T AS t", "t.")]
    [InlineData("spacing.afterDot", "SELECT t. Col FROM dbo.T AS t", ".Col")]
    [InlineData("spacing.beforeScopeResolution", "SELECT geometry :: STGeomFromText('POINT (1 1)', 0)", "geometry::")]
    [InlineData("spacing.afterScopeResolution", "SELECT geometry :: STGeomFromText('POINT (1 1)', 0)", "::STGeomFromText")]
    [InlineData("spacing.beforeFunctionArguments", "SELECT ABS (1)", "ABS(")]
    [InlineData("spacing.withinEmptyFunctionArguments", "SELECT GETDATE( )", "GETDATE()")]
    [InlineData("spacing.withinFunctionArguments", "SELECT ABS( 1 )", "ABS(1)")]
    [InlineData("spacing.arithmeticOperators", "SELECT 1 + 2", "1+2")]
    public void Remove_rule_changes_only_requested_boundary(string key, string source, string expected)
    {
        var text = Format(source, "\"" + key + "\":\"remove\"");
        Assert.Contains(expected, text);
    }

    [Fact]
    public void Literal_comment_and_unary_minus_are_not_rewritten()
    {
        var text = Format("SELECT -1+2, 'a,b.c' -- x+y, z\n",
            "\"spacing.arithmeticOperators\":\"insert\",\"spacing.afterComma\":\"remove\"");
        Assert.Contains("-1 + 2", text);
        Assert.Contains("'a,b.c'", text);
        Assert.Contains("-- x+y, z", text);
    }

    [Fact]
    public void All_ten_spacing_paths_are_registered()
    {
        Assert.Equal(10, RuleCatalog.Default.Definitions.Keys.Count(key => key.StartsWith("spacing.", StringComparison.Ordinal)));
    }
}
