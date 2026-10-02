using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlPromptAdditionalObjectsTests
{
    private static FormattingOptions Profile => new SqlFormatterConfigurationSerializer().Deserialize(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NativeProfiles", "ADIR_SQL_Main.json")));

    [Theory]
    [InlineData("inherit", true)]
    [InlineData("always", true)]
    [InlineData("multiple", true)]
    [InlineData("never", false)]
    [InlineData("ifLong", false)]
    public void Function_parameter_wrapping_is_independent_of_procedure_parameters(string mode, bool stacked)
    {
        var options = Profile.With(rules: Profile.Rules.With("layout.functionParameters", RuleValue.FromChoice(mode)));
        const string source = "CREATE FUNCTION dbo.AddNumbers(@first INT, @second INT) RETURNS INT AS BEGIN RETURN @first + @second; END;";
        var text = Verify(source, options);
        Assert.Equal(stacked, !text.Contains("@first INT, @second INT)", StringComparison.Ordinal));
        var procedure = Verify("CREATE PROCEDURE dbo.AddNumbers @first INT, @second INT AS SELECT @first + @second;", options);
        Assert.Contains("\n    @first", procedure);
        Assert.Contains("\n    , @second", procedure);
    }

    [Fact]
    public void Long_function_headers_expand_and_align_types_and_defaults()
    {
        const string source = "CREATE FUNCTION dbo.CalculateTotal(@firstParameterWithLongName INT = 1, @secondParameterWithLongName DECIMAL(18, 8) = 2, @thirdParameterWithLongName VARCHAR(100) = 'Sample', @fourthParameterWithLongName BIGINT = 4) RETURNS INT AS BEGIN RETURN 1; END;";
        var text = Verify(source);
        Assert.StartsWith("CREATE FUNCTION dbo.CalculateTotal\n(\n    @firstParameterWithLongName", text);
        Assert.Contains("\n)\nRETURNS", text);
        var lines = text.Split('\n').Where(l => l.Contains(" = ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, lines.Length);
        Assert.Single(lines.Select(l => l.IndexOf('=')).Distinct());
    }

    [Fact]
    public void Parameter_comments_force_a_safe_multiline_function_header()
    {
        var text = Verify("CREATE FUNCTION dbo.F(@id INT, -- keep parameter meaning\n @name VARCHAR(20)) RETURNS INT AS BEGIN RETURN @id; END;");
        Assert.Contains("-- keep parameter meaning", text);
        Assert.StartsWith("CREATE FUNCTION dbo.F\n(", text);
    }

    [Fact]
    public void Temporal_period_and_inline_indexes_keep_their_own_closing_parentheses()
    {
        var text = Verify("CREATE TABLE dbo.HistoryItems (Id INT NOT NULL, ValidFrom DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL, ValidTo DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL, PERIOD FOR SYSTEM_TIME(ValidFrom, ValidTo), INDEX IX_Items NONCLUSTERED (Id ASC));");
        Assert.Contains("PERIOD FOR SYSTEM_TIME(ValidFrom, ValidTo)", text);
        Assert.Contains("INDEX IX_Items NONCLUSTERED (Id ASC)", text);
        Assert.Contains("\n);", text);
    }

    [Fact]
    public void Derived_table_alias_columns_do_not_capture_the_query_closing_parenthesis()
    {
        var text = Verify("SELECT d.Id FROM (SELECT Id FROM dbo.Items WHERE Description IS NOT NULL AND Amount > 0 AND StatusCode = 'A sufficiently long status description to expand the derived query') AS d(Id);");
        Assert.Contains(") AS d(Id);", text);
        Assert.DoesNotContain("d(Id\n", text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Column_constraint_alignment_can_be_disabled_independently(bool align)
    {
        var options = Profile.With(rules: Profile.Rules.With("layout.alignDdlConstraints", RuleValue.FromBoolean(align)).With("layout.parenthesesCompact", RuleValue.FromThreshold(new ThresholdRule(false, 120))));
        var text = Verify("CREATE TABLE dbo.Items (Id INT NOT NULL, Description VARCHAR(30) NULL, Amount DECIMAL(18, 2) NOT NULL);", options);
        var lines = text.Split('\n').Where(l => l.Contains(" NULL", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, lines.Length);
        var positions = lines.Select(l => l.IndexOf("NOT NULL", StringComparison.Ordinal) is var i && i >= 0
            ? i : l.IndexOf("NULL", StringComparison.Ordinal)).Distinct().Count();
        Assert.Equal(align ? 1 : 3, positions);
    }

    [Fact]
    public void Column_constraints_can_align_without_type_alignment()
    {
        var options = Profile.With(rules: Profile.Rules.With("layout.alignDdlTypes", RuleValue.FromBoolean(false))
            .With("layout.parenthesesCompact", RuleValue.FromThreshold(new ThresholdRule(false, 120))));
        var text = Verify("CREATE TABLE dbo.Items (Id INT NOT NULL, Description VARCHAR(30) NULL, Amount DECIMAL(18, 2) NOT NULL);", options);
        var lines = text.Split('\n').Where(l => l.Contains(" NULL", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Single(lines.Select(l => l.IndexOf("NOT NULL", StringComparison.Ordinal) is var i && i >= 0
            ? i : l.IndexOf("NULL", StringComparison.Ordinal)).Distinct());
        Assert.Contains("Id INT", text);
        Assert.Contains("Description VARCHAR(30)", text);
    }

    [Fact]
    public void Join_boolean_operators_indent_from_on_and_where_from_the_query()
    {
        var text = Verify("CREATE VIEW dbo.ItemsView AS SELECT i.Id, i.Description FROM dbo.Items AS i INNER JOIN dbo.OtherItems AS o ON o.Id = i.Id AND o.Status = 'Active' WHERE i.Amount > 0 AND i.Status = 'Available';");
        Assert.Contains("\n        ON o.Id = i.Id\n            AND o.Status = 'Active'", text);
        Assert.Contains("\n    WHERE i.Amount > 0\n        AND i.Status = 'Available'", text);
    }

    [Fact]
    public void New_layout_settings_are_disabled_by_default_and_survive_profile_exchange()
    {
        Assert.Equal("inherit", FormattingOptions.Default.Rules.Get("layout.functionParameters").Choice);
        Assert.False(FormattingOptions.Default.Rules.Get("layout.alignDdlConstraints").Boolean);
        var editor = new SettingsEditorModel(Profile);
        var restored = new SettingsEditorModel();
        restored.Import(editor.Export());
        Assert.Equal("ifLong", restored.Options.Rules.Get("layout.functionParameters").Choice);
        Assert.True(restored.Options.Rules.Get("layout.alignDdlConstraints").Boolean);
    }

    private static string Verify(string source, FormattingOptions? options = null)
    {
        var formatter = new ScriptDomSqlFormatter();
        options ??= Profile;
        var first = formatter.Format(source, options, new FormatRequest());
        Assert.True(first.ParseSucceeded);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error);
        Assert.Equal(first.Text, formatter.Format(first.Text, options, new FormatRequest()).Text);
        var parser = new ScriptDomSqlParser();
        Assert.Equal(Tokens(parser.Parse(source, SqlDialectVersion.Auto)), Tokens(parser.Parse(first.Text, SqlDialectVersion.Auto)));
        return first.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Tokens(SqlParseResult p) => p.Tokens
        .Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile))
        .Select(t => t.TokenType is TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral
            or TSqlTokenType.QuotedIdentifier or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment
            ? t.Text : t.Text.ToUpperInvariant());
}
