using TSqlFormatter.Core.Navigation;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class NavigationTests
{
    private readonly ISqlParser _parser = new ScriptDomSqlParser();
    private readonly ScriptOutlineBuilder _outlineBuilder = new();
    private readonly SymbolLocator _symbolLocator = new();

    private static string TitleTree(ScriptOutlineNode node) =>
        node.Title + (node.Children.Count == 0
            ? string.Empty
            : "[" + string.Join(",", node.Children.Select(TitleTree)) + "]");

    [Fact]
    public void Outline_builds_batches_statements_ctes_and_variables()
    {
        const string sql = "DECLARE @id INT, @name NVARCHAR(50);\n" +
                           "WITH recent AS (SELECT Id FROM dbo.Orders WHERE CreatedAt > '2026-01-01')\n" +
                           "SELECT Id FROM recent;\n" +
                           "GO\n" +
                           "UPDATE dbo.T SET a = 1 WHERE Id = @id;";

        var outline = _outlineBuilder.Build(_parser.Parse(sql, SqlDialectVersion.Auto));

        Assert.NotNull(outline);
        Assert.Equal(ScriptOutlineNodeKind.Script, outline!.Kind);
        var batches = outline.Children;
        Assert.Equal(2, batches.Count);
        Assert.Equal("Batch 1", batches[0].Title);
        Assert.Equal("Batch 2", batches[1].Title);

        var declare = batches[0].Children[0];
        Assert.Equal("DECLARE", declare.Title);
        Assert.Equal(2, declare.Children.Count);
        Assert.Equal("@id", declare.Children[0].Title);
        Assert.Equal("@name", declare.Children[1].Title);
        Assert.Equal(sql.IndexOf("@name", StringComparison.Ordinal), declare.Children[1].Span.StartOffset);

        var select = batches[0].Children[1];
        Assert.Equal("SELECT", select.Title);
        var cte = Assert.Single(select.Children);
        Assert.Equal(ScriptOutlineNodeKind.Cte, cte.Kind);
        Assert.Equal("CTE recent", cte.Title);
        Assert.Equal(sql.IndexOf("recent", StringComparison.Ordinal), cte.Span.StartOffset);

        Assert.Equal("UPDATE", batches[1].Children[0].Title);
        Assert.Empty(batches[1].Children[0].Children);
    }

    [Fact]
    public void Outline_names_modules_with_their_bodies()
    {
        const string sql = "CREATE OR ALTER PROCEDURE dbo.DoWork @n INT AS BEGIN SELECT @n; END\n" +
                           "GO\n" +
                           "CREATE FUNCTION dbo.Calc(@x INT) RETURNS INT AS BEGIN RETURN @x; END";

        var outline = _outlineBuilder.Build(_parser.Parse(sql, SqlDialectVersion.Auto));

        Assert.NotNull(outline);
        var procedure = outline!.Children[0].Children[0];
        Assert.Equal(ScriptOutlineNodeKind.Module, procedure.Kind);
        Assert.Equal("PROCEDURE dbo.DoWork", procedure.Title);
        Assert.Equal(sql.IndexOf("dbo.DoWork", StringComparison.Ordinal), procedure.Span.StartOffset);
        var procedureBody = Assert.Single(procedure.Children);
        Assert.Equal("BEGIN...END", procedureBody.Title);
        Assert.Equal("SELECT", Assert.Single(procedureBody.Children).Title);

        var function = outline.Children[1].Children[0];
        Assert.Equal("FUNCTION dbo.Calc", function.Title);
        var functionBody = Assert.Single(function.Children);
        Assert.Equal("BEGIN...END", functionBody.Title);
        Assert.Equal("RETURN", Assert.Single(functionBody.Children).Title);
    }

    [Fact]
    public void Outline_describes_begin_end_blocks()
    {
        const string sql = "IF 1 = 1 BEGIN PRINT 'a'; SELECT 1; END";

        var outline = _outlineBuilder.Build(_parser.Parse(sql, SqlDialectVersion.Auto));

        Assert.Equal("IF[BEGIN...END[PRINT,SELECT]]", TitleTree(outline!.Children[0].Children[0]));
    }

    [Fact]
    public void Outline_returns_null_for_failed_parse()
    {
        var parseResult = new SqlParseResult("x", SqlDialectVersion.Auto,
            Microsoft.SqlServer.TransactSql.ScriptDom.SqlVersion.Sql180,
            root: null, Array.Empty<Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken>(),
            Array.Empty<ParseDiagnostic>());

        Assert.Null(_outlineBuilder.Build(parseResult));
        Assert.Throws<ArgumentNullException>(() => _outlineBuilder.Build(null!));
    }

    [Fact]
    public void Locator_finds_nearest_declaration_above()
    {
        const string sql = "DECLARE @x INT;\nSET @x = 1;\nDECLARE @x BIGINT;\nSELECT @x;";

        var span = _symbolLocator.FindVariableDeclaration(_parser.Parse(sql, SqlDialectVersion.Auto),
            sql.IndexOf("@x", 3, StringComparison.Ordinal) + 1);

        Assert.NotNull(span);
        Assert.Equal(sql.IndexOf("@x", StringComparison.Ordinal), span!.Value.StartOffset);
    }

    [Fact]
    public void Locator_prefers_the_closest_declaration()
    {
        const string sql = "DECLARE @x INT;\nDECLARE @x BIGINT;\nSELECT @x;";

        var span = _symbolLocator.FindVariableDeclaration(_parser.Parse(sql, SqlDialectVersion.Auto),
            sql.LastIndexOf("@x", StringComparison.Ordinal));

        Assert.NotNull(span);
        Assert.Equal(sql.IndexOf("@x", 20, StringComparison.Ordinal), span!.Value.StartOffset);
    }

    [Fact]
    public void Locator_ignores_declarations_below_and_other_batches()
    {
        const string below = "SELECT @x;\nDECLARE @x INT;";
        var parseBelow = _parser.Parse(below, SqlDialectVersion.Auto);
        Assert.Null(_symbolLocator.FindVariableDeclaration(parseBelow, below.IndexOf("@x", StringComparison.Ordinal)));

        const string otherBatch = "DECLARE @x INT;\nGO\nSELECT @x;";
        var parseOther = _parser.Parse(otherBatch, SqlDialectVersion.Auto);
        Assert.Null(_symbolLocator.FindVariableDeclaration(parseOther,
            otherBatch.LastIndexOf("@x", StringComparison.Ordinal)));
    }

    [Fact]
    public void Locator_requires_a_variable_token()
    {
        const string sql = "DECLARE @x INT;\nSELECT Id FROM dbo.T;";

        Assert.Null(_symbolLocator.FindVariableDeclaration(
            _parser.Parse(sql, SqlDialectVersion.Auto), sql.IndexOf("dbo", StringComparison.Ordinal)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _symbolLocator.FindVariableDeclaration(
            _parser.Parse(sql, SqlDialectVersion.Auto), -1));
        Assert.Throws<ArgumentNullException>(() => _symbolLocator.FindVariableDeclaration(null!, 0));
    }
}
