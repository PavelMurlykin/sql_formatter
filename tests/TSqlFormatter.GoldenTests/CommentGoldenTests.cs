using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.GoldenTests;

public sealed class CommentGoldenTests
{
    private const string Marker = "__COMMENT__";

    private static readonly string[] Payloads =
    {
        "note", "alpha beta", "сортировка", "id:42", "x--y"
    };

    private static readonly (string Name, string Source, string Expected)[] Shapes =
    {
        ("header line", "-- __COMMENT__\nselect Id from T",
            "-- __COMMENT__\nSELECT Id\nFROM T"),
        ("column line", "select Id, -- __COMMENT__\nName from T",
            "SELECT\n    Id, -- __COMMENT__\n    Name\nFROM T"),
        ("before from line", "select Id\n-- __COMMENT__\nfrom T",
            "SELECT Id\n-- __COMMENT__\nFROM T"),
        ("before where line", "select Id from T\n-- __COMMENT__\nwhere Id=1",
            "SELECT Id\nFROM T\n-- __COMMENT__\nWHERE\n    Id = 1"),
        ("before order line", "select Id from T\n-- __COMMENT__\norder by Id",
            "SELECT Id\nFROM T\n-- __COMMENT__\nORDER BY Id"),
        ("column block", "select Id, /* __COMMENT__ */ Name from T",
            "SELECT\n    Id, /* __COMMENT__ */\n    Name\nFROM T"),
        ("before from block", "select Id\n/* __COMMENT__ */\nfrom T",
            "SELECT Id\n/* __COMMENT__ */\nFROM T"),
        ("before where block", "select Id from T\n/* __COMMENT__ */\nwhere Id=1",
            "SELECT Id\nFROM T\n/* __COMMENT__ */\nWHERE\n    Id = 1"),
        ("before order block", "select Id from T\n/* __COMMENT__ */\norder by Id",
            "SELECT Id\nFROM T\n/* __COMMENT__ */\nORDER BY Id"),
        ("standalone block", "/* __COMMENT__ */\n\nselect Id from T",
            "/* __COMMENT__ */\n\nSELECT Id\nFROM T")
    };

    public static IEnumerable<object[]> Cases =>
        from shape in Shapes
        from payload in Payloads
        select new object[]
        {
            shape.Name + ": " + payload,
            shape.Source.Replace(Marker, payload),
            shape.Expected.Replace(Marker, payload),
            payload
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Comment_is_preserved_once_and_result_is_stable(
        string name, string source, string expected, string payload)
    {
        var formatter = new ScriptDomSqlFormatter();
        var options = new FormattingOptions();
        var first = formatter.Format(source, options, new FormatRequest());
        var second = formatter.Format(first.Text, options, new FormatRequest());

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.Equal(expected, first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(1, Count(first.Text, payload));
        Assert.Equal(first.Text, second.Text);
        Assert.False(second.Changed);
    }

    [Fact]
    public void Has_at_least_fifty_distinct_golden_cases()
    {
        var cases = Cases.ToArray();
        Assert.True(cases.Length >= 50);
        Assert.Equal(cases.Length, cases.Select(item => item[1]).Distinct().Count());
    }

    private static int Count(string source, string fragment) =>
        source.Split(new[] { fragment }, StringSplitOptions.None).Length - 1;
}
