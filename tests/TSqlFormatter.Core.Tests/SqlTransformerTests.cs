using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;
using TSqlFormatter.Core.Refactoring;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlTransformerTests
{
    private readonly ISqlParser _parser = new ScriptDomSqlParser();

    private SqlTransformationResult Transform(SqlTransformationKind kind, string sql, string marker)
    {
        var index = sql.IndexOf(marker, StringComparison.Ordinal);
        return SqlTransformer.Transform(kind, sql, index < 0 ? 0 : index, FormattingOptions.Default);
    }

    private void AssertResultParses(SqlTransformationResult result)
    {
        Assert.True(result.Succeeded, result.Error);
        var reparsed = _parser.Parse(result.NewDocumentText, SqlDialectVersion.Auto);
        Assert.True(reparsed.ParseSucceeded,
            reparsed.Diagnostics.Count > 0 ? reparsed.Diagnostics[0].Message : "parse failed");
    }

    [Fact]
    public void InsertToUpdate_produces_update_with_set_and_where_stub()
    {
        const string sql = "INSERT INTO dbo.T (a, b) VALUES (1, 'x');";

        var result = Transform(SqlTransformationKind.InsertToUpdate, sql, "dbo.T");

        AssertResultParses(result);
        Assert.Contains("UPDATE", result.NewDocumentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a = 1", result.NewDocumentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("b = 'x'", result.NewDocumentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TODO", result.NewStatementText, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InsertToUpdate_replaces_only_the_statement()
    {
        const string sql = "SELECT 1;\nINSERT INTO dbo.T (a) VALUES (2);\nSELECT 3;";

        var result = Transform(SqlTransformationKind.InsertToUpdate, sql, "dbo.T");

        AssertResultParses(result);
        Assert.Equal(sql.IndexOf("INSERT", StringComparison.Ordinal), result.ReplacedSpan.StartOffset);
        Assert.DoesNotContain("INSERT", result.NewDocumentText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.T VALUES (1);", "column list")]
    [InlineData("INSERT INTO dbo.T (a, b) VALUES (1, 2), (3, 4);", "exactly one")]
    [InlineData("INSERT INTO dbo.T DEFAULT VALUES;", "VALUES")]
    [InlineData("SELECT 1;", "INSERT")]
    public void InsertToUpdate_rejects_unsupported_inputs(string sql, string expectedFragment)
    {
        var result = Transform(SqlTransformationKind.InsertToUpdate, sql, "dbo.T");

        Assert.False(result.Succeeded);
        Assert.Contains(expectedFragment, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValuesToUnionAll_builds_union_chain_with_aliases_on_first_row()
    {
        const string sql = "INSERT INTO dbo.T (a, b) VALUES (1, 'x'), (2, 'y');";

        var result = Transform(SqlTransformationKind.ValuesToUnionAll, sql, "dbo.T");

        AssertResultParses(result);
        Assert.Contains("UNION ALL", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS a", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS b", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'y'", result.NewStatementText, StringComparison.Ordinal);
    }

    [Fact]
    public void ValuesToUnionAll_supports_a_single_row()
    {
        const string sql = "INSERT INTO dbo.T (a) VALUES (1);";

        var result = Transform(SqlTransformationKind.ValuesToUnionAll, sql, "dbo.T");

        AssertResultParses(result);
        Assert.DoesNotContain("UNION", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS a", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.T VALUES (1);", "column list")]
    [InlineData("INSERT INTO dbo.T (a) VALUES (1, 2);", "same length")]
    [InlineData("SELECT 1;", "INSERT")]
    public void ValuesToUnionAll_rejects_unsupported_inputs(string sql, string expectedFragment)
    {
        var result = Transform(SqlTransformationKind.ValuesToUnionAll, sql, "dbo.T");

        Assert.False(result.Succeeded);
        Assert.Contains(expectedFragment, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecWithReturnCode_declares_and_captures_the_code()
    {
        const string sql = "EXEC dbo.DoWork @a, 5;";

        var result = Transform(SqlTransformationKind.ExecWithReturnCode, sql, "DoWork");

        AssertResultParses(result);
        Assert.Contains("DECLARE @returnCode AS INT", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@returnCode = dbo.DoWork", result.NewStatementText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("IF 1 = 0 EXEC dbo.DoWork;", "then")]
    [InlineData("IF 1 = 0 SELECT 1; ELSE EXEC dbo.DoWork;", "else")]
    [InlineData("WHILE 1 = 0 EXEC dbo.DoWork;", "while")]
    public void ExecWithReturnCode_keeps_both_statements_inside_the_controlled_body(string sql, string branch)
    {
        var result = Transform(SqlTransformationKind.ExecWithReturnCode, sql, "EXEC");

        AssertResultParses(result);
        var script = Assert.IsType<TSqlScript>(_parser.Parse(result.NewDocumentText, SqlDialectVersion.Auto).Root);
        var outer = Assert.Single(Assert.Single(script.Batches).Statements);
        var body = branch switch
        {
            "then" => Assert.IsType<IfStatement>(outer).ThenStatement,
            "else" => Assert.IsType<IfStatement>(outer).ElseStatement,
            _ => Assert.IsType<WhileStatement>(outer).Statement
        };
        var statements = Assert.IsType<BeginEndBlockStatement>(body).StatementList.Statements;
        Assert.Collection(statements,
            statement => Assert.IsType<DeclareVariableStatement>(statement),
            statement => Assert.IsType<ExecuteStatement>(statement));
    }

    [Theory]
    [InlineData("[Order Id]", "Order Id")]
    [InlineData("[select]", "select")]
    [InlineData("[a]]b]", "a]b")]
    [InlineData("\"Order Id\"", "Order Id")]
    public void ValuesToUnionAll_preserves_delimited_column_aliases(string identifier, string expectedName)
    {
        var sql = $"INSERT INTO dbo.T ({identifier}) VALUES (1), (2);";

        var result = Transform(SqlTransformationKind.ValuesToUnionAll, sql, "INSERT");

        AssertResultParses(result);
        var script = Assert.IsType<TSqlScript>(_parser.Parse(result.NewDocumentText, SqlDialectVersion.Auto).Root);
        var select = Assert.IsType<SelectStatement>(Assert.Single(Assert.Single(script.Batches).Statements));
        var union = Assert.IsType<BinaryQueryExpression>(select.QueryExpression);
        var first = Assert.IsType<QuerySpecification>(union.FirstQueryExpression);
        var alias = Assert.IsType<SelectScalarExpression>(Assert.Single(first.SelectElements)).ColumnName.Identifier;
        Assert.Equal(expectedName, alias.Value);
        Assert.NotEqual(QuoteType.NotQuoted, alias.QuoteType);
    }

    [Fact]
    public void ValuesToUnionAll_rejects_a_result_that_does_not_parse()
    {
        var result = Transform(SqlTransformationKind.ValuesToUnionAll,
            "INSERT INTO dbo.T (a) VALUES (DEFAULT);", "INSERT");

        Assert.False(result.Succeeded);
        Assert.Empty(result.NewDocumentText);
    }

    [Theory]
    [InlineData("DECLARE @rv INT; EXEC @rv = dbo.P;", "already captures")]
    [InlineData("SELECT 1;", "EXEC")]
    public void ExecWithReturnCode_rejects_unsupported_inputs(string sql, string expectedFragment)
    {
        var result = Transform(SqlTransformationKind.ExecWithReturnCode, sql, "dbo.P");

        Assert.False(result.Succeeded);
        Assert.Contains(expectedFragment, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transform_reports_parse_and_caret_errors()
    {
        var parseError = SqlTransformer.Transform(SqlTransformationKind.InsertToUpdate,
            "select from", 0, FormattingOptions.Default);
        Assert.False(parseError.Succeeded);
        Assert.Contains("parsed", parseError.Error, StringComparison.OrdinalIgnoreCase);

        var caretError = SqlTransformer.Transform(SqlTransformationKind.InsertToUpdate,
            "SELECT 1;", 1, FormattingOptions.Default);
        Assert.False(caretError.Succeeded);
        Assert.Contains("statement", caretError.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transform_validates_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => SqlTransformer.Transform(
            SqlTransformationKind.InsertToUpdate, null!, 0, FormattingOptions.Default));
        Assert.Throws<ArgumentNullException>(() => SqlTransformer.Transform(
            SqlTransformationKind.InsertToUpdate, "SELECT 1;", 0, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => SqlTransformer.Transform(
            SqlTransformationKind.InsertToUpdate, "SELECT 1;", -1, FormattingOptions.Default));
    }
}
