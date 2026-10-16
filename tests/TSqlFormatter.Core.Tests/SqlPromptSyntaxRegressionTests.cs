using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlPromptSyntaxRegressionTests
{
    private static FormattingOptions Profile => new SqlFormatterConfigurationSerializer().Deserialize(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NativeProfiles", "ADIR_SQL_Main.json")));

    [Theory]
    [InlineData("USE [WorkDb]", "USE WorkDb;")]
    [InlineData("SELECT [ItemId] FROM [WorkDb].[dbo].[Items]", "FROM WorkDb.dbo.Items;")]
    [InlineData("EXEC [WorkDb].[dbo].[ReadItems]", "EXEC WorkDb.dbo.ReadItems;")]
    [InlineData("USE[WorkDb]", "USE WorkDb;")]
    public void Profile_removes_optional_object_brackets_and_terminates_statements(string source, string expected)
    {
        var result = Format(source);
        Assert.Contains(expected, result);
        Assert.DoesNotContain("[", result);
    }

    [Theory]
    [InlineData("IF @x=1 SELECT 1 ELSE IF @x=2 SELECT 2 ELSE IF @x=3 SELECT 3 ELSE SELECT 4")]
    [InlineData("IF @x=1 BEGIN SELECT 1 END ELSE IF @x=2 BEGIN SELECT 2 END ELSE IF @x=3 BEGIN SELECT 3 END ELSE BEGIN SELECT 4 END")]
    [InlineData("IF @var = 1\nBEGIN\n\tSELECT 1\nEND\n\tELSE IF @var = 2\n\tBEGIN\n\t\tSELECT 2\n\tEND\n\t\tELSE IF @var = 3\n\t\tBEGIN\n\t\t\tSELECT 3\n\t\tEND\nELSE SELECT 4")]
    public void Else_if_chain_keeps_every_else_at_the_original_if_column(string source)
    {
        var text = Format(source);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var alternatives = lines.Where(line => line.TrimStart().StartsWith("ELSE", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, alternatives.Length);
        Assert.All(alternatives, line => Assert.StartsWith("ELSE", line));
        Assert.Equal(2, alternatives.Count(line => line.StartsWith("ELSE IF", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("SELECT [Group], [Order Details], [a]]b], [1st], [@NotAVariable] FROM [dbo].[Items]", "[Group]")]
    [InlineData("SELECT N'[dbo].[Items];', [NormalName] FROM [dbo].[Items] -- [keep];", "N'[dbo].[Items];'")]
    [InlineData("SELECT [Код] FROM [База].[Схема].[Таблица]", "База.Схема.Таблица;")]
    [InlineData("SELECT [Id] FROM [dbo].[#TempItems]", "dbo.#TempItems;")]
    [InlineData("SELECT \"QuotedName\" FROM [dbo].[Items]", "\"QuotedName\"")]
    public void Optional_brackets_do_not_change_required_quotes_literals_or_comments(string source, string preserved)
    {
        string text = Format(source);
        Assert.Contains(preserved, text);
        Assert.DoesNotContain(new ScriptDomSqlParser().Parse(text, SqlDialectVersion.Auto).Tokens,
            t => t.TokenType == TSqlTokenType.QuotedIdentifier && t.Text == "[dbo]");
        if (source.Contains("[Order Details]"))
            foreach (string name in new[] { "[Order Details]", "[a]]b]", "[1st]", "[@NotAVariable]" }) Assert.Contains(name, text);
        if (source.Contains("--")) Assert.Contains("-- [keep];", text);
    }

    [Fact]
    public void Adjacent_bracketed_column_and_alias_keep_separate_identifiers()
    {
        string text = Format("SELECT [ItemId][ItemAlias] FROM [dbo].[Items]");
        Assert.Contains("ItemId ItemAlias", text);
        var statement = Assert.IsType<SelectStatement>(Assert.IsType<TSqlScript>(new ScriptDomSqlParser()
            .Parse(text, SqlDialectVersion.Auto).Root).Batches.Single().Statements.Single());
        var column = Assert.IsType<SelectScalarExpression>(Assert.IsType<QuerySpecification>(statement.QueryExpression).SelectElements.Single());
        Assert.Equal("ItemAlias", column.ColumnName.Identifier.Value);
        Assert.Equal("ItemId", Assert.IsType<ColumnReferenceExpression>(column.Expression).MultiPartIdentifier.Identifiers.Single().Value);
    }

    [Theory]
    [InlineData("USE [WorkDb]\nGO\nSELECT 1 -- keep\nGO\nPRINT 'x'", 3)]
    [InlineData("CREATE FUNCTION [dbo].[ReadItems]() RETURNS TABLE AS RETURN (SELECT [Id] FROM [dbo].[Items])", 1)]
    [InlineData("CREATE VIEW [dbo].[ItemView] AS SELECT [Id] FROM [dbo].[Items]", 1)]
    [InlineData("INSERT INTO [dbo].[Items] EXEC [dbo].[ReadItems]", 1)]
    [InlineData("retry: SELECT 1 GOTO retry", 2)]
    [InlineData("BEGIN TRY SELECT 1 END TRY BEGIN CATCH PRINT 'error' END CATCH", 3)]
    [InlineData("SELECT 1; SELECT 2;;", 3)]
    [InlineData("CREATE FUNCTION [dbo].[AddNumbers](@first INT, @second INT) RETURNS INT AS BEGIN RETURN @first + @second END", 2)]
    [InlineData("CREATE FUNCTION dbo.AddNumbers(@first INT, @second INT) RETURNS INT AS BEGIN RETURN @first + @second; END;", 2)]
    public void Semicolons_use_executable_statement_boundaries_and_preserve_existing_terminators(string source, int expectedCount)
    {
        string text = Format(source);
        var parsed = new ScriptDomSqlParser().Parse(text, SqlDialectVersion.Auto);
        Assert.Equal(expectedCount, parsed.Tokens.Count(t => t.Text == ";"));
        Assert.DoesNotContain("GO;", text);
        Assert.DoesNotContain("retry:;", text);
        if (source.Contains("--")) Assert.Contains("1; -- keep", text);
    }

    [Fact]
    public void Defaults_retain_brackets_and_missing_terminators_and_each_rule_can_be_disabled()
    {
        const string source = "USE [WorkDb]";
        var formatter = new ScriptDomSqlFormatter();
        Assert.Contains("[WorkDb]", formatter.Format(source, FormattingOptions.Default, new FormatRequest()).Text);
        Assert.DoesNotContain(";", formatter.Format(source, FormattingOptions.Default, new FormatRequest()).Text);
        var brackets = Profile.With(rules: Profile.Rules.With("syntax.removeOptionalBrackets", RuleValue.FromBoolean(false)));
        Assert.Contains("[WorkDb];", formatter.Format(source, brackets, new FormatRequest()).Text);
        var semicolons = Profile.With(rules: Profile.Rules.With("syntax.addSemicolons", RuleValue.FromBoolean(false)));
        Assert.Contains("USE WorkDb", formatter.Format(source, semicolons, new FormatRequest()).Text);
        Assert.DoesNotContain(";", formatter.Format(source, semicolons, new FormatRequest()).Text);
    }

    [Fact]
    public void Bracket_removal_composes_with_explicit_identifier_and_alias_casing()
    {
        const string source = "SELECT [ItemId][ItemAlias] FROM [dbo].[Items]";
        var options = Profile.With(rules: Profile.Rules.With("textCase.identifier", RuleValue.FromChoice("upper"))
            .With("textCase.alias", RuleValue.FromChoice("lower")));
        var formatter = new ScriptDomSqlFormatter();
        var result = formatter.Format(source, options, new FormatRequest());
        Assert.True(result.Changed);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "TSF3006" or "TSF3007");
        Assert.Contains("ITEMID itemalias", result.Text);
        Assert.Contains("DBO.ITEMS;", result.Text);
        Assert.Equal(result.Text, formatter.Format(result.Text, options, new FormatRequest()).Text);
        var selection = new EditorSqlFormatter().Format(source, options, new SqlTextSpan(source.IndexOf("[ItemId]"), "[ItemId]".Length));
        Assert.Equal("ITEMID", selection.Edits.Single().NewText);
        Assert.Contains("[ItemAlias] FROM [dbo].[Items]", selection.Text);
    }

    [Fact]
    public void Disabled_regions_keep_optional_brackets_and_missing_semicolons()
    {
        const string region = "\nSELECT [Id] FROM [dbo].[Items]\n";
        string source = "-- SQL Prompt formatting off" + region + "-- SQL Prompt formatting on\nSELECT [Id] FROM [dbo].[Items]";
        string text = Format(source);
        Assert.Contains("-- SQL Prompt formatting off" + region + "-- SQL Prompt formatting on", text);
        Assert.Contains("FROM dbo.Items;", text);
    }

    [Theory]
    [InlineData("SELECT [Id] FROM [dbo].[Items] WHERE [Id]=1", "WHERE [Id]=1", "WHERE Id = 1")]
    [InlineData("SELECT [Id] FROM [dbo].[Items] WHERE [Id]=1", "[Id]", "Id")]
    [InlineData("USE [FirstDb]\nUSE [SecondDb]", "USE [SecondDb]", "USE SecondDb;")]
    public void Selection_keeps_outside_text_and_does_not_terminate_an_enclosing_statement(string source, string selection, string expected)
    {
        int start = source.IndexOf(selection, StringComparison.Ordinal);
        var result = new EditorSqlFormatter().Format(source, Profile, new SqlTextSpan(start, selection.Length));
        Assert.True(result.Changed, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.StartsWith(source.Substring(0, start), result.Text);
        Assert.EndsWith(source.Substring(start + selection.Length), result.Text);
        Assert.Equal(expected, result.Edits.Single().NewText);
    }

    [Theory]
    [InlineData("CREATE VIEW [dbo].[ItemView] AS SELECT [Id] FROM [dbo].[Items]", "SELECT [Id] FROM [dbo].[Items]", 0)]
    [InlineData("CREATE FUNCTION [dbo].[ReadItems]() RETURNS TABLE AS RETURN (SELECT [Id] FROM [dbo].[Items])", "SELECT [Id] FROM [dbo].[Items]", 0)]
    [InlineData("SELECT [First] FROM [dbo].[Items] WHERE [Id]=1\nSELECT [Second] FROM [dbo].[Items]", "WHERE [Id]=1\nSELECT [Second] FROM [dbo].[Items]", 1)]
    public void Contextual_selection_terminates_only_complete_executable_statements(string source, string selection, int terminators)
    {
        int start = source.IndexOf(selection, StringComparison.Ordinal);
        var result = new EditorSqlFormatter().Format(source, Profile, new SqlTextSpan(start, selection.Length));
        Assert.True(result.Changed);
        Assert.StartsWith(source.Substring(0, start), result.Text);
        Assert.EndsWith(source.Substring(start + selection.Length), result.Text);
        var replacement = result.Edits.Single().NewText;
        Assert.Contains("FROM dbo.Items", replacement);
        Assert.Equal(terminators, new ScriptDomSqlParser().Parse(result.Text, SqlDialectVersion.Auto).Tokens.Count(t => t.Text == ";"));
        Assert.True(new ScriptDomSqlParser().Parse(result.Text, SqlDialectVersion.Auto).ParseSucceeded);
    }

    [Fact]
    public void Real_nested_if_keeps_its_indent_while_the_outer_else_chain_stays_flat()
    {
        string text = Format("IF @outer=1 BEGIN IF @inner=1 SELECT 1 ELSE SELECT 2 END ELSE IF @outer=2 SELECT 3 ELSE SELECT 4");
        Assert.Contains("\r\n    IF @inner", text);
        Assert.Contains("\r\n    ELSE\r\n", text);
        Assert.Contains("\r\nELSE IF @outer", text);
    }

    [Fact]
    public void User_reported_chain_matches_the_requested_layout()
    {
        const string source = "IF @var = 1\nBEGIN\n\tSELECT 1\nEND\n\tELSE IF @var = 2\n\tBEGIN\n\t\tSELECT 2\n\tEND\n\t\tELSE IF @var = 3\n\t\tBEGIN\n\t\t\tSELECT 3\n\t\tEND";
        const string expected = "IF @var = 1\nBEGIN\n    SELECT 1;\nEND;\nELSE IF @var = 2\nBEGIN\n    SELECT 2;\nEND;\nELSE IF @var = 3\nBEGIN\n    SELECT 3;\nEND;";
        Assert.Equal(expected, Format(source).Replace("\r\n", "\n").TrimEnd());
    }

    [Fact]
    public void Token_validation_accepts_only_the_requested_bracket_and_terminator_edits()
    {
        var parser = new ScriptDomSqlParser();
        var before = parser.Parse("SELECT [Id] FROM [dbo].[Items]", SqlDialectVersion.Auto);
        var allowed = parser.Parse("SELECT Id FROM dbo.Items;", SqlDialectVersion.Auto);
        Assert.True(SqlTokenSafety.PreservesTokens(before, allowed, Profile));
        Assert.False(SqlTokenSafety.PreservesTokens(before, allowed, FormattingOptions.Default));
        Assert.False(SqlTokenSafety.PreservesTokens(before, parser.Parse("SELECT OtherId FROM dbo.Items;", SqlDialectVersion.Auto), Profile));
        Assert.False(SqlTokenSafety.PreservesTokens(before, parser.Parse("SELECT Id FROM dbo.Items;;", SqlDialectVersion.Auto), Profile));
    }

    private static string Format(string source)
    {
        var formatter = new ScriptDomSqlFormatter();
        var result = formatter.Format(source, Profile, new FormatRequest());
        Assert.True(result.ParseSucceeded);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "TSF3006" or "TSF3007");
        var parser = new ScriptDomSqlParser();
        Assert.True(SqlTokenSafety.PreservesTokens(parser.Parse(source, SqlDialectVersion.Auto), parser.Parse(result.Text, SqlDialectVersion.Auto), Profile));
        Assert.Equal(result.Text, formatter.Format(result.Text, Profile, new FormatRequest()).Text);
        return result.Text;
    }
}
