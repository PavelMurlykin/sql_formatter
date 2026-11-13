using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;
using TSqlFormatter.Core.QueryResults;

namespace TSqlFormatter.Core.Tests;

public sealed class QueryResultsTests
{
    private static string In(string source, bool headers = false, bool text = false, FormattingOptions? formatting = null) =>
        QueryResultSql.CreateIn(TsvTable.Parse(source), new(headers, text), formatting ?? FormattingOptions.Default);
    private static string Insert(string source, bool headers = false, string target = "dbo.Target", bool text = false) =>
        QueryResultSql.CreateInsert(TsvTable.Parse(source), target, new(headers, text), FormattingOptions.Default);
    private static TSqlFragment Parse(string sql)
    {
        var result = new ScriptDomSqlParser().Parse(sql, SqlDialectVersion.Auto);
        Assert.True(result.ParseSucceeded, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        return result.Root!;
    }
    private sealed class Collector<T> : TSqlFragmentVisitor where T : TSqlFragment
    {
        internal List<T> Items { get; } = new();
        public override void Visit(TSqlFragment node) { if (node is T item) Items.Add(item); }
    }
    private static IReadOnlyList<T> Find<T>(TSqlFragment root) where T : TSqlFragment
    { var visitor = new Collector<T>(); root.Accept(visitor); return visitor.Items; }
    private static InPredicate Predicate(string snippet) => Assert.Single(Find<InPredicate>(Parse("SELECT 1 WHERE x " + snippet + ";")));
    private static void Error(ResultDataError code, Action action) => Assert.Equal(code, Assert.Throws<ResultDataException>(action).Code);

    [Theory]
    [InlineData("\n")][InlineData("\r\n")][InlineData("\r")]
    public void Tsv_preserves_empty_cells_unicode_and_one_final_record_separator(string newline)
    {
        var table = TsvTable.Parse("Имя\t\tДата" + newline + "Анна\t\tNULL" + newline);
        Assert.Equal(2, table.Rows.Count); Assert.Equal(3, table.ColumnCount);
        Assert.Equal("", table.Rows[1][1].Text); Assert.Equal("NULL", table.Rows[1][2].Text);
    }
    [Fact]
    public void Quoted_tsv_decodes_tabs_newlines_and_escaped_quotes_without_losing_type_intent()
    {
        var table = TsvTable.Parse("\"a\tb\"\t\"line1\r\nline2\"\t\"say \"\"hi\"\"\"\t\"\"\n");
        Assert.Single(table.Rows); Assert.Equal(4, table.ColumnCount);
        Assert.Equal("a\tb", table.Rows[0][0].Text); Assert.Equal("line1\r\nline2", table.Rows[0][1].Text);
        Assert.Equal("say \"hi\"", table.Rows[0][2].Text); Assert.Equal("", table.Rows[0][3].Text);
        Assert.All(table.Rows[0], c => Assert.True(c.WasQuoted));
        Assert.Equal("a\"b", TsvTable.Parse("a\"b").Rows[0][0].Text);
    }
    [Theory]
    [InlineData("", ResultDataError.EmptyInput)]
    [InlineData("a\0b", ResultDataError.MalformedTsv)]
    [InlineData("\"unfinished", ResultDataError.MalformedTsv)]
    [InlineData("\"finished\"extra", ResultDataError.MalformedTsv)]
    [InlineData("a\tb\nc", ResultDataError.UnequalColumns)]
    public void Malformed_tsv_is_rejected_without_partial_rows(string source, ResultDataError error) => Error(error, () => TsvTable.Parse(source));

    [Fact]
    public void Tsv_limits_bound_characters_rows_columns_and_cells()
    {
        Error(ResultDataError.TooLarge, () => TsvTable.Parse(new string('x', TsvTable.MaxCharacters + 1)));
        Error(ResultDataError.TooManyRows, () => TsvTable.Parse(string.Join("\n", Enumerable.Repeat("x", TsvTable.MaxRows + 1))));
        Error(ResultDataError.TooManyColumns, () => TsvTable.Parse(string.Join("\t", Enumerable.Repeat("x", TsvTable.MaxColumns + 1))));
        string row = string.Join("\t", Enumerable.Repeat("x", 100));
        Error(ResultDataError.TooManyCells, () => TsvTable.Parse(string.Join("\n", Enumerable.Repeat(row, 1001))));
        Assert.Equal(1000, TsvTable.Parse(string.Join("\n", Enumerable.Repeat(row, 1000))).Rows.Count);
        Assert.Equal(10000, TsvTable.Parse(string.Join("\n", Enumerable.Repeat("x", 10000))).Rows.Count);
    }
    [Fact]
    public void In_is_a_formatted_fragment_and_keeps_numeric_values_nulls_and_duplicates()
    {
        string sql = In("-1\n+2.500\n0\nNULL\n0");
        Assert.StartsWith("IN", sql); Assert.DoesNotContain("SELECT", sql);
        var values = Predicate(sql).Values;
        Assert.Equal(5, values.Count); Assert.IsType<NullLiteral>(values[3]);
        Assert.Equal("2.5", Assert.IsAssignableFrom<Literal>(values[1]).Value);
        Assert.Equal("0", Assert.IsAssignableFrom<Literal>(values[4]).Value);
    }
    [Theory]
    [InlineData("1\ntext", "1", "text")]
    [InlineData("001\n2", "001", "2")]
    [InlineData("\"1\"\n2", "1", "2")]
    [InlineData("1\n\"\"", "1", "")]
    [InlineData("1,5\n2", "1,5", "2")]
    [InlineData("1e3\n2", "1e3", "2")]
    public void Mixed_or_ambiguous_columns_stay_unicode_text(string source, string first, string second)
    {
        var values = Predicate(In(source)).Values;
        Assert.Equal(first, Assert.IsType<StringLiteral>(values[0]).Value);
        Assert.Equal(second, Assert.IsType<StringLiteral>(values[1]).Value);
        Assert.All(values, value => Assert.True(Assert.IsType<StringLiteral>(value).IsNational));
    }
    [Fact]
    public void String_escaping_cannot_add_sql_statements_and_preserves_unicode()
    {
        const string value = "О'Брайен'); DROP TABLE dbo.T;--";
        var root = Parse("SELECT 1 WHERE x " + In(value) + ";");
        Assert.Empty(Find<DropTableStatement>(root));
        Assert.Equal(value, Assert.Single(Find<StringLiteral>(root)).Value);
    }
    [Fact]
    public void Empty_quoted_null_and_unquoted_null_have_distinct_meanings()
    {
        var values = Predicate(In("\"\"\n\"NULL\"\nNULL\n NULL ")).Values;
        Assert.Equal("", Assert.IsType<StringLiteral>(values[0]).Value);
        Assert.Equal("NULL", Assert.IsType<StringLiteral>(values[1]).Value);
        Assert.IsType<NullLiteral>(values[2]); Assert.Equal(" NULL ", Assert.IsType<StringLiteral>(values[3]).Value);
        Assert.All(Predicate(In("1\nNULL\n2026-11-13", text: true)).Values, value => Assert.IsType<StringLiteral>(value));
    }
    [Fact]
    public void Iso_dates_and_datetimes_use_explicit_conversion_styles()
    {
        string sql = In("2026-11-13\n2026-11-13 12:34:56.1234567\nNULL");
        var calls = Find<ConvertCall>(Parse("SELECT 1 WHERE x " + sql + ";"));
        Assert.Equal(2, calls.Count); Assert.Contains("23", sql); Assert.Contains("126", sql);
        Assert.Equal("2026-11-13T12:34:56.1234567", Assert.IsType<StringLiteral>(calls[1].Parameter).Value);
    }
    [Theory]
    [InlineData("2026-02-30")][InlineData("13.11.2026")][InlineData("2026-11-13T12:34:56.")][InlineData("2026-11-13T12:34:56Z")]
    public void Invalid_or_unsupported_dates_are_preserved_as_text(string source) =>
        Assert.Equal(source, Assert.IsType<StringLiteral>(Assert.Single(Predicate(In(source)).Values)).Value);
    [Fact]
    public void Header_handling_is_explicit_and_in_does_not_flatten_multiple_columns()
    {
        Assert.Equal(2, Predicate(In("1\n2")).Values.Count);
        Assert.Single(Predicate(In("Id\n1", headers: true)).Values);
        Error(ResultDataError.InRequiresOneColumn, () => In("1\t2"));
        Error(ResultDataError.NoDataRows, () => In("Id", headers: true));
        Error(ResultDataError.NoDataRows, () => Insert("Id", headers: true));
        Error(ResultDataError.NoDataRows, () => TsvStatistics.Calculate(TsvTable.Parse("Id"), true));
    }
    [Fact]
    public void Insert_uses_explicit_header_columns_and_independent_column_types()
    {
        string sql = Insert("Id\tName\tDate\n1\tO'Brien\t2026-11-13\n2\tNULL\t2026-11-14", headers: true);
        var insert = Assert.Single(Find<InsertStatement>(Parse(sql))).InsertSpecification;
        Assert.Equal(new[] { "Id", "Name", "Date" }, insert.Columns.Select(c => c.MultiPartIdentifier.Identifiers.Single().Value));
        var rows = Assert.IsType<ValuesInsertSource>(insert.InsertSource).RowValues;
        Assert.Equal(2, rows.Count); Assert.IsType<IntegerLiteral>(rows[0].ColumnValues[0]);
        Assert.Equal("O'Brien", Assert.IsType<StringLiteral>(rows[0].ColumnValues[1]).Value);
        Assert.IsType<ConvertCall>(rows[0].ColumnValues[2]); Assert.IsType<NullLiteral>(rows[1].ColumnValues[1]);
        Assert.Empty(Assert.Single(Find<InsertStatement>(Parse(Insert("1\tName")))).InsertSpecification.Columns);
    }
    [Fact]
    public void Headers_and_target_names_are_identifiers_even_when_they_contain_sql_punctuation()
    {
        const string header = "x]; DROP TABLE dbo.T;--";
        var root = Parse(Insert(header + "\nvalue", headers: true, target: "[data.base].[dbo].[T]];x]"));
        Assert.Empty(Find<DropTableStatement>(root));
        var insert = Assert.Single(Find<InsertStatement>(root)).InsertSpecification;
        Assert.Equal(header, insert.Columns[0].MultiPartIdentifier.Identifiers.Single().Value);
        Assert.Equal("T];x", Assert.IsType<NamedTableReference>(insert.Target).SchemaObject.BaseIdentifier.Value);
    }
    [Theory]
    [InlineData("")][InlineData("dbo..T")][InlineData("dbo.T;DROP TABLE T;")][InlineData("dbo.T--comment")]
    [InlineData("[unclosed")][InlineData("a.b.c.d")][InlineData("dbo.")][InlineData("[x] suffix")]
    public void Table_dialog_input_cannot_inject_sql(string target) => Error(ResultDataError.InvalidTable, () => Insert("1", target: target));
    [Theory]
    [InlineData("dbo.T", "[dbo].[T]")][InlineData(" [db] . [dbo] . [Order] ", "[db].[dbo].[Order]")]
    [InlineData("#temporary", "[#temporary]")][InlineData("[a]]b]", "[a]]b]")]
    public void Valid_table_names_are_normalized(string input, string expected) => Assert.Equal(expected, QueryResultSql.NormalizeTableName(input));
    [Theory]
    [InlineData("A\tA\n1\t2")][InlineData("A\ta\n1\t2")][InlineData("A\t\n1\t2")][InlineData("\"A\nB\"\n1")]
    public void Duplicate_empty_or_control_character_headers_are_rejected(string source) => Error(ResultDataError.InvalidHeaders, () => Insert(source, headers: true));
    [Fact]
    public void Oversized_identifiers_are_rejected()
    {
        Error(ResultDataError.InvalidHeaders, () => Insert(new string('x', 129) + "\n1", headers: true));
        Error(ResultDataError.InvalidTable, () => Insert("1", target: "[" + new string('x', 129) + "]"));
    }
    [Fact]
    public void Insert_splits_1001_rows_without_dropping_or_reordering_values()
    {
        var inserts = Find<InsertStatement>(Parse(Insert(string.Join("\n", Enumerable.Range(1, 1001)))));
        Assert.Equal(2, inserts.Count);
        var rows = inserts.SelectMany(i => Assert.IsType<ValuesInsertSource>(i.InsertSpecification.InsertSource).RowValues).ToArray();
        Assert.Equal(1000, Assert.IsType<ValuesInsertSource>(inserts[0].InsertSpecification.InsertSource).RowValues.Count);
        Assert.Equal(1001, rows.Length); Assert.Equal("1001", Assert.IsType<IntegerLiteral>(rows[1000].ColumnValues[0]).Value);
    }
    [Fact]
    public void Combined_numeric_precision_does_not_silently_reduce_column_scale()
    {
        string big = new('9', 38);
        var values = Predicate(In(big + "\n0.1")).Values;
        Assert.Equal(big, Assert.IsType<StringLiteral>(values[0]).Value); Assert.IsType<StringLiteral>(values[1]);
    }
    [Fact]
    public void In_and_insert_follow_keyword_case_in_the_supplied_profile()
    {
        var formatting = new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Lower));
        Assert.StartsWith("in", In("1\n2", formatting: formatting));
        Assert.StartsWith("insert", QueryResultSql.CreateInsert(TsvTable.Parse("1"), "dbo.T", new(), formatting));
    }
    [Theory]
    [InlineData("ADIR_SQL_Main")][InlineData("AV_Profile")][InlineData("Right-aligned-EPM-AWB2")]
    public void Clipboard_sql_respects_each_shipped_native_profile(string name)
    {
        var profile = new TSqlFormatter.Configuration.SqlFormatterProfileExchange().Import(Path.Combine(AppContext.BaseDirectory, "NativeProfiles", name + ".json"));
        Assert.True(profile.Succeeded);
        Assert.Equal(3, Predicate(In("1\n2\nNULL", formatting: profile.Options)).Values.Count);
        string sql = QueryResultSql.CreateInsert(TsvTable.Parse("Id\tName\tDate\n1\tO'Brien\t2026-11-13"),
            "[dbo].[Target]", new(firstRowHeaders: true), profile.Options!);
        Assert.Single(Find<InsertStatement>(Parse(sql)));
        Assert.Equal("O'Brien", Assert.Single(Find<StringLiteral>(Parse(sql)).Where(l => l.Value == "O'Brien")).Value);
    }
    [Theory]
    [InlineData("ru-RU")][InlineData("en-US")]
    public void Statistics_are_invariant_and_count_numeric_cells_across_columns(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var result = TsvStatistics.Calculate(TsvTable.Parse("A\tB\n1\t2.5\nNULL\t-3\n4\tfoo"), true);
            Assert.Equal(6, result.TotalCells); Assert.Equal(4, result.NumericCount); Assert.Equal(2, result.SkippedCount);
            Assert.Equal(4.5m, result.Sum); Assert.Equal(1.125m, result.Average);
            Assert.Equal("2.5", Assert.IsAssignableFrom<Literal>(Assert.Single(Predicate(In("+2.500")).Values)).Value);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void Statistics_decode_quotes_trim_numeric_whitespace_and_do_not_guess_locale_or_exponents()
    {
        var result = TsvStatistics.Calculate(TsvTable.Parse("\"1.5\"\t 2 \n1,5\t1e3"));
        Assert.Equal(2, result.NumericCount); Assert.Equal(2, result.SkippedCount); Assert.Equal(3.5m, result.Sum);
        var empty = TsvStatistics.Calculate(TsvTable.Parse("text\tNULL\t\"\""));
        Assert.Equal(0, empty.NumericCount); Assert.Equal(0, empty.Sum); Assert.Null(empty.Average);
    }
    [Theory]
    [InlineData("79228162514264337593543950336")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("79228162514264337593543950335\n1")]
    [InlineData("4000000000000000000000000000.1\n4000000000000000000000000000.1")]
    public void Statistics_never_return_partial_or_rounded_sums(string source) =>
        Error(ResultDataError.NumericOverflow, () => TsvStatistics.Calculate(TsvTable.Parse(source)));
    [Fact]
    public void Statistics_accumulate_exactly_before_checking_the_final_sum_range()
    {
        var result = TsvStatistics.Calculate(TsvTable.Parse("79228162514264337593543950335\n1\n-1"));
        Assert.Equal(decimal.MaxValue, result.Sum); Assert.Equal(3, result.NumericCount);
    }
    [Fact]
    public void Cancellation_aborts_parsing_generation_and_statistics()
    {
        var token = new CancellationToken(true); var table = TsvTable.Parse("1");
        Assert.Throws<OperationCanceledException>(() => TsvTable.Parse("1", token));
        Assert.Throws<OperationCanceledException>(() => QueryResultSql.CreateIn(table, new(), FormattingOptions.Default, token));
        Assert.Throws<OperationCanceledException>(() => QueryResultSql.CreateInsert(table, "dbo.T", new(), FormattingOptions.Default, token));
        Assert.Throws<OperationCanceledException>(() => TsvStatistics.Calculate(table, false, token));
    }
}
