using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;
using TSqlFormatter.Core.Refactoring;

namespace TSqlFormatter.Core.Tests;

public sealed class RenameAnalyzerTests
{
    private readonly ISqlParser _parser = new ScriptDomSqlParser();
    private readonly RenameAnalyzer _analyzer = new();

    private RenameSymbol? Find(string sql, string symbolText, int occurrence = 0, int delta = 0)
    {
        var index = -1;
        for (var match = 0; match <= occurrence; match++)
        {
            index = sql.IndexOf(symbolText, index + 1, StringComparison.Ordinal);
        }

        var parseResult = _parser.Parse(sql, SqlDialectVersion.Auto);
        return _analyzer.FindSymbol(parseResult, index + delta);
    }

    private static void AssertAllSpansCover(string sql, RenameSymbol symbol, string expectedText)
    {
        Assert.All(symbol.Occurrences, span =>
            Assert.Equal(expectedText, sql.Substring(span.StartOffset, span.Length)));
    }

    [Fact]
    public void Variable_under_caret_collects_declaration_and_references()
    {
        const string sql = "DECLARE @custId INT = 1; SELECT @custId FROM dbo.Orders WHERE Id = @custId;";

        var symbol = Find(sql, "@custId", occurrence: 1);

        Assert.NotNull(symbol);
        Assert.Equal(RenameSymbolKind.LocalVariable, symbol.Kind);
        Assert.Equal("@custId", symbol!.Name);
        Assert.Equal(3, symbol.Occurrences.Count);
        AssertAllSpansCover(sql, symbol, "@custId");
    }

    [Fact]
    public void Variable_scope_stops_at_batch_boundary()
    {
        const string sql = "DECLARE @x INT;\nGO\nDECLARE @x INT;\nSELECT @x;";

        var symbol = Find(sql, "@x", occurrence: 2);

        Assert.NotNull(symbol);
        Assert.Equal(2, symbol!.Occurrences.Count);
        var secondBatchOffset = sql.IndexOf("GO", StringComparison.Ordinal);
        Assert.All(symbol.Occurrences, span => Assert.True(span.StartOffset > secondBatchOffset));
    }

    [Fact]
    public void Variable_rename_ignores_other_variables()
    {
        const string sql = "DECLARE @a INT, @b INT; SELECT @a, @b;";

        var symbol = Find(sql, "@a");

        Assert.NotNull(symbol);
        Assert.Equal(2, symbol!.Occurrences.Count);
        AssertAllSpansCover(sql, symbol, "@a");
    }

    [Fact]
    public void Alias_in_from_collects_alias_and_qualifiers()
    {
        const string sql = "SELECT t.Id FROM dbo.T AS t WHERE t.Amount > 0;";

        var symbol = Find(sql, "AS t", delta: 3);

        Assert.NotNull(symbol);
        Assert.Equal(RenameSymbolKind.TableAlias, symbol!.Kind);
        Assert.Equal("t", symbol.Name);
        Assert.Equal(3, symbol.Occurrences.Count);
        AssertAllSpansCover(sql, symbol, "t");
    }

    [Fact]
    public void Alias_under_qualifier_finds_the_same_symbol()
    {
        const string sql = "SELECT t.Id FROM dbo.T AS t WHERE t.Amount > 0;";

        var symbol = Find(sql, "t.Amount");

        Assert.NotNull(symbol);
        Assert.Equal(3, symbol!.Occurrences.Count);
    }

    [Fact]
    public void Alias_rename_stays_within_one_query()
    {
        const string sql = "SELECT t.Id FROM dbo.A AS t;\nSELECT t.Id FROM dbo.B AS t;";

        var symbol = Find(sql, "AS t", delta: 3);

        Assert.NotNull(symbol);
        Assert.Equal(2, symbol!.Occurrences.Count);
        Assert.All(symbol.Occurrences, span => Assert.True(span.StartOffset < sql.IndexOf('\n')));
    }

    [Fact]
    public void Shadowing_alias_in_nested_query_is_not_renamed()
    {
        const string sql = "SELECT t.Id FROM dbo.A AS t WHERE EXISTS (SELECT 1 FROM dbo.B AS t WHERE t.Id = 1);";

        var symbol = Find(sql, "AS t", delta: 3);

        Assert.NotNull(symbol);
        Assert.Equal(2, symbol!.Occurrences.Count);
        var nestedStart = sql.IndexOf("dbo.B", StringComparison.Ordinal);
        Assert.All(symbol.Occurrences, span => Assert.True(span.StartOffset < nestedStart));
    }

    [Fact]
    public void Correlated_reference_to_outer_alias_is_renamed()
    {
        const string sql = "SELECT c.Name FROM dbo.Customers AS c WHERE EXISTS (SELECT 1 FROM dbo.Orders AS o WHERE o.CustomerId = c.Id);";

        var symbol = Find(sql, "AS c", delta: 3);

        Assert.NotNull(symbol);
        Assert.Equal(3, symbol!.Occurrences.Count);
        AssertAllSpansCover(sql, symbol, "c");
    }

    [Fact]
    public void Quoted_alias_is_not_supported()
    {
        const string sql = "SELECT [t].Id FROM dbo.T AS [t];";

        var symbol = Find(sql, "AS [t]");

        Assert.Null(symbol);
    }

    [Fact]
    public void Alias_without_query_specification_is_not_supported()
    {
        const string sql = "UPDATE t SET Amount = 1 FROM dbo.T AS t;";

        var symbol = Find(sql, "AS t", delta: 3);

        Assert.Null(symbol);
    }

    [Fact]
    public void Offset_without_symbol_returns_null()
    {
        const string sql = "SELECT Id FROM dbo.T WHERE Id = 1;";

        var symbol = Find(sql, "WHERE");

        Assert.Null(symbol);
    }

    [Fact]
    public void Null_root_returns_null()
    {
        var parseResult = new SqlParseResult(
            "SELECT 1", SqlDialectVersion.Auto, SqlVersion.Sql180,
            root: null, Array.Empty<TSqlParserToken>(), Array.Empty<ParseDiagnostic>());

        Assert.Null(_analyzer.FindSymbol(parseResult, 0));
    }

    [Fact]
    public void Plan_maps_occurrences_and_validates_names()
    {
        const string sql = "DECLARE @old INT; SELECT @old;";
        var symbol = Find(sql, "@old");

        var edits = _analyzer.Plan(symbol!, "@newName");

        Assert.Equal(2, edits.Count);
        Assert.All(edits, edit => Assert.Equal("@newName", edit.NewText));
        Assert.Equal("@old", sql.Substring(edits[0].Span.StartOffset, edits[0].Span.Length));

        Assert.Throws<ArgumentException>(() => _analyzer.Plan(symbol!, "newName"));
        Assert.Throws<ArgumentException>(() => _analyzer.Plan(symbol!, "@1bad"));
    }

    [Fact]
    public void Plan_for_alias_rejects_variable_name()
    {
        const string sql = "SELECT t.Id FROM dbo.T AS t;";
        var symbol = Find(sql, "AS t", delta: 3);

        Assert.Throws<ArgumentException>(() => _analyzer.Plan(symbol!, "@t"));
        Assert.Equal(2, _analyzer.Plan(symbol!, "t2").Count);
    }

    [Theory]
    [InlineData("SELECT")]
    [InlineData("select")]
    [InlineData("FROM")]
    [InlineData("WHERE")]
    [InlineData("ORDER")]
    [InlineData("UNION")]
    public void Alias_plan_rejects_reserved_words_before_producing_edits(string name)
    {
        var symbol = Find("SELECT t.Id FROM dbo.T AS t;", "t.Id");

        Assert.False(RenameNames.IsValidIdentifier(name));
        Assert.Throws<ArgumentException>(() => _analyzer.Plan(symbol!, name));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("_source2")]
    [InlineData("Источник")]
    public void Alias_plan_produces_parseable_sql_for_valid_names(string name)
    {
        const string sql = "SELECT t.Id FROM dbo.T AS t WHERE t.Id > 0;";
        var symbol = Find(sql, "t.Id");
        var changed = sql;
        foreach (var edit in _analyzer.Plan(symbol!, name).OrderByDescending(edit => edit.Span.StartOffset))
        {
            changed = changed.Remove(edit.Span.StartOffset, edit.Span.Length).Insert(edit.Span.StartOffset, edit.NewText);
        }

        Assert.True(_parser.Parse(changed, SqlDialectVersion.Auto).ParseSucceeded);
    }

    [Fact]
    public void Variable_name_may_contain_a_keyword_but_names_must_fit_the_sql_limit()
    {
        Assert.True(RenameNames.IsValidVariableName("@select"));
        Assert.True(RenameNames.IsValidIdentifier(new string('a', 128)));
        Assert.False(RenameNames.IsValidIdentifier(new string('a', 129)));
        Assert.True(RenameNames.IsValidVariableName("@" + new string('a', 127)));
        Assert.False(RenameNames.IsValidVariableName("@" + new string('a', 128)));
    }

    [Fact]
    public void Alias_plan_covers_select_and_where_qualifiers()
    {
        const string sql = "SELECT t.Id FROM dbo.T AS t WHERE t.Amount > 0;";
        var symbol = Find(sql, "AS t", delta: 3);

        var edits = _analyzer.Plan(symbol!, "src");

        Assert.Equal(3, edits.Count);
        Assert.All(edits, edit => Assert.Equal("src", edit.NewText));
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => _analyzer.FindSymbol(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _analyzer.FindSymbol(
            _parser.Parse("SELECT 1;", SqlDialectVersion.Auto), -1));
        Assert.Throws<ArgumentNullException>(() => _analyzer.Plan(null!, "x"));
    }

    [Theory]
    [InlineData("@ok", true)]
    [InlineData("@a1_#", true)]
    [InlineData("@", false)]
    [InlineData("ok", false)]
    [InlineData("@1a", false)]
    [InlineData("@a b", false)]
    [InlineData("", false)]
    public void Variable_name_validation(string name, bool expected)
    {
        Assert.Equal(expected, RenameNames.IsValidVariableName(name));
    }

    [Theory]
    [InlineData("t2", true)]
    [InlineData("_x", true)]
    [InlineData("#tmp", true)]
    [InlineData("@x", false)]
    [InlineData("1x", false)]
    [InlineData("a-b", false)]
    [InlineData("", false)]
    public void Identifier_name_validation(string name, bool expected)
    {
        Assert.Equal(expected, RenameNames.IsValidIdentifier(name));
    }
}
