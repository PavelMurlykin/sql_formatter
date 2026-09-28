using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Core.Tests;

public sealed class StoredCodeFormattingTests
{
    private readonly ScriptDomSqlFormatter formatter = new();

    [Theory]
    [InlineData("declare @a int,@b varchar(20);", "DECLARE @a int,\n    @b varchar(20);")]
    [InlineData("set @a=1;", "SET @a = 1;")]
    [InlineData("throw 50001,'bad',1;", "THROW 50001, 'bad', 1;")]
    [InlineData("begin set @a=1; set @b=2; end", "BEGIN\n    SET @a = 1;\n    SET @b = 2;\nEND")]
    [InlineData("if @a=1 begin set @b=2; end else set @b=3;", "IF @a=1\nBEGIN\n    SET @b = 2;\nEND\nELSE\n    SET @b = 3;")]
    [InlineData("while @a<2 set @a=@a+1;", "WHILE @a<2\n    SET @a = @a+1;")]
    [InlineData("begin try set @a=1; end try begin catch throw; end catch", "BEGIN TRY\n    SET @a = 1;\nEND TRY\nBEGIN CATCH\n    THROW;\nEND CATCH")]
    public void Formats_simple_control_flow(string source, string expected)
    {
        var first = formatter.Format(source, FormattingOptions.Default, new FormatRequest());
        var second = formatter.Format(first.Text, FormattingOptions.Default, new FormatRequest());

        Assert.True(first.ParseSucceeded);
        Assert.Equal(expected, first.Text);
        Assert.Equal(first.Text, second.Text);
    }

    [Theory]
    [InlineData("create procedure dbo.p as begin select a from T; end",
        "CREATE PROCEDURE dbo.p\nAS\nBEGIN\n    SELECT a\n    FROM T;\nEND")]
    [InlineData("create function dbo.f() returns int as begin return 1; end",
        "CREATE FUNCTION dbo.f() returns int\nAS\nBEGIN\n    RETURN 1;\nEND")]
    [InlineData("create view dbo.v as select a from T;",
        "CREATE VIEW dbo.v\nAS\nSELECT a\nFROM T;")]
    public void Formats_simple_modules(string source, string expected)
    {
        var first = formatter.Format(source, FormattingOptions.Default, new FormatRequest());
        var second = formatter.Format(first.Text, FormattingOptions.Default, new FormatRequest());

        Assert.True(first.ParseSucceeded);
        Assert.Equal(expected, first.Text);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Leaves_commented_control_flow_layout_untouched()
    {
        const string source = "begin /* keep */ set @a=1; end";
        var result = formatter.Format(source, FormattingOptions.Default, new FormatRequest());

        Assert.True(result.ParseSucceeded);
        Assert.Equal("BEGIN /* keep */ SET @a=1; END", result.Text);
    }
}
