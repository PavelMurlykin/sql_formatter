using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Generation;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class CrudGenerationTests
{
    private readonly ISqlParser _parser = new ScriptDomSqlParser();
    private readonly TableDefinitionReader _reader = new();

    private const string TableSql = "CREATE TABLE dbo.Orders (" +
        "Id INT IDENTITY(1, 1) NOT NULL PRIMARY KEY, " +
        "Name NVARCHAR(50) NOT NULL, " +
        "Amount DECIMAL(18, 2) NULL, " +
        "Notes NVARCHAR(MAX) NULL);";

    private SqlTableDefinition? Read(string sql) => _reader.Read(_parser.Parse(sql, SqlDialectVersion.Auto));

    private static void AssertParses(string sql)
    {
        var reparsed = new ScriptDomSqlParser().Parse(sql, SqlDialectVersion.Auto);
        Assert.True(reparsed.ParseSucceeded,
            reparsed.Diagnostics.Count > 0 ? reparsed.Diagnostics[0].Message : "parse failed");
    }

    [Fact]
    public void Reader_parses_schema_columns_types_nullability_and_identity()
    {
        var table = Read(TableSql);

        Assert.NotNull(table);
        Assert.Equal("dbo", table!.Schema);
        Assert.Equal("Orders", table.Name);
        Assert.Equal("dbo.Orders", table.DisplayName);
        Assert.Equal(4, table.Columns.Count);

        var id = table.Columns[0];
        Assert.Equal("Id", id.Name);
        Assert.Equal("INT", id.DataType);
        Assert.False(id.IsNullable);
        Assert.True(id.IsIdentity);

        var name = table.Columns[1];
        Assert.Equal("NVARCHAR(50)", name.DataType);
        Assert.False(name.IsNullable);

        Assert.Equal("DECIMAL(18, 2)", table.Columns[2].DataType);
        Assert.True(table.Columns[2].IsNullable);
        Assert.Equal("NVARCHAR(MAX)", table.Columns[3].DataType);
        Assert.True(table.Columns[3].IsNullable);
    }

    [Fact]
    public void Reader_defaults_schema_and_unspecified_nullability()
    {
        var table = Read("CREATE TABLE Logs (Message VARCHAR(100));");

        Assert.NotNull(table);
        Assert.Equal(string.Empty, table!.Schema);
        Assert.Equal("Logs", table.Name);
        Assert.Equal("Logs", table.DisplayName);
        Assert.Equal("dbo", table.SchemaOrDbo);
        Assert.True(table.Columns[0].IsNullable);
    }

    [Fact]
    public void Reader_scopes_to_the_requested_span()
    {
        const string sql = "CREATE TABLE dbo.A (Id INT);\nSELECT 1;\nCREATE TABLE dbo.B (Id INT);";

        var parseResult = _parser.Parse(sql, SqlDialectVersion.Auto);
        var scope = new SqlTextSpan(sql.IndexOf("CREATE TABLE dbo.B", StringComparison.Ordinal), 40);

        var table = _reader.Read(parseResult, scope);

        Assert.NotNull(table);
        Assert.Equal("B", table!.Name);
        Assert.Equal(sql.IndexOf("CREATE TABLE dbo.B", StringComparison.Ordinal),
            table.StatementSpan.StartOffset);
    }

    [Fact]
    public void Reader_returns_null_without_a_create_table()
    {
        Assert.Null(Read("SELECT 1;"));

        var parseResult = new SqlParseResult("x", SqlDialectVersion.Auto,
            Microsoft.SqlServer.TransactSql.ScriptDom.SqlVersion.Sql180,
            root: null, Array.Empty<Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken>(),
            Array.Empty<ParseDiagnostic>());
        Assert.Null(_reader.Read(parseResult));
        Assert.Throws<ArgumentNullException>(() => _reader.Read(null!));
    }

    [Fact]
    public void Generator_produces_all_four_statements_and_a_procedure()
    {
        var table = Read(TableSql);

        var sql = CrudGenerator.Generate(table!, FormattingOptions.Default);

        AssertParses(sql);
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM dbo.Orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INSERT INTO dbo.Orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name = @Name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Amount = @Amount", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DELETE FROM dbo.Orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("usp_Orders_Upsert", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Name NVARCHAR(50)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Amount DECIMAL(18, 2)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Notes NVARCHAR(MAX) = NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generator_excludes_identity_columns_from_writable_parts()
    {
        var table = Read(TableSql);

        var sql = CrudGenerator.Generate(table!, FormattingOptions.Default);

        AssertParses(sql);
        Assert.DoesNotContain("@Id", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO dbo.Orders (Id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Id = @Id", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generator_skips_insert_and_update_without_writable_columns()
    {
        var table = Read("CREATE TABLE dbo.S (NextId INT IDENTITY(1, 1));");

        var sql = CrudGenerator.Generate(table!, FormattingOptions.Default);

        AssertParses(sql);
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE OR ALTER PROCEDURE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generator_validates_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => CrudGenerator.Generate(null!, FormattingOptions.Default));
        Assert.Throws<ArgumentNullException>(() => CrudGenerator.Generate(
            Read(TableSql)!, null!));
    }
}
