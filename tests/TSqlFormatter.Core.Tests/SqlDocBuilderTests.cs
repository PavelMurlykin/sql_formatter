using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlDocBuilderTests
{
    private readonly ScriptDomSqlParser _parser = new();
    private readonly DocRenderer _renderer = new();

    [Theory]
    [InlineData("")]
    [InlineData("SELECT 1;")]
    [InlineData("-- lead\r\nSELECT 1;  -- tail\r\n")]
    [InlineData("SELECT 'text -- literal';\nSELECT [from];")]
    [InlineData("SELECT 1;\r\nGO\r\nSELECT 2;")]
    public void DefaultBuilders_PreserveEntireValidSource(string source)
    {
        var result = _parser.Parse(source, SqlDialectVersion.Auto);
        var builder = new SqlDocBuilder();

        Assert.True(result.ParseSucceeded);
        Assert.Equal(source, _renderer.Render(builder.BuildDocument(result)));
    }

    [Fact]
    public void ParseFailure_ProducesUnchangedSourceWithoutInvokingCustomBuilder()
    {
        const string source = "SELECT FROM; -- keep this";
        var result = _parser.Parse(source, SqlDialectVersion.Auto);
        var custom = new ReplacingSelectBuilder();
        var builder = new SqlDocBuilder(new ISqlFragmentDocBuilder[] { custom });

        Assert.False(result.ParseSucceeded);
        Assert.Equal(source, _renderer.Render(builder.BuildDocument(result)));
        Assert.Equal(0, custom.BuildCount);
    }

    [Fact]
    public void CustomStatementBuilder_PreservesCommentsAndGapsAroundStatements()
    {
        const string source = "-- lead\r\nSELECT 1;  -- gap\r\nSELECT 2; -- tail";
        var result = _parser.Parse(source, SqlDialectVersion.Auto);
        var custom = new ReplacingSelectBuilder();
        var builder = new SqlDocBuilder(new ISqlFragmentDocBuilder[] { custom });

        var rendered = _renderer.Render(builder.BuildDocument(result));

        Assert.Equal("-- lead\r\n[stmt]  -- gap\r\n[stmt] -- tail", rendered);
        Assert.Equal(2, custom.BuildCount);
    }

    [Fact]
    public void BuildFragment_ReturnsOriginalStatementSpan()
    {
        const string source = "-- lead\nSELECT 1; -- tail";
        var result = _parser.Parse(source, SqlDialectVersion.Auto);
        var script = Assert.IsType<TSqlScript>(result.Root);
        var statement = Assert.Single(Assert.Single(script.Batches).Statements);

        var document = new SqlDocBuilder().BuildFragment(result, statement);

        Assert.Equal("SELECT 1;", _renderer.Render(document));
    }

    [Fact]
    public void EarlierRegisteredBuilder_TakesPrecedence()
    {
        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        var first = new ReplacingSelectBuilder("first");
        var second = new ReplacingSelectBuilder("second");
        var builder = new SqlDocBuilder(new ISqlFragmentDocBuilder[] { first, second });

        Assert.Equal("first", _renderer.Render(builder.BuildDocument(result)));
        Assert.Equal(1, first.BuildCount);
        Assert.Equal(0, second.BuildCount);
    }

    [Fact]
    public void BuilderList_IsCopiedWhenRegistered()
    {
        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        var custom = new ReplacingSelectBuilder();
        var registered = new List<ISqlFragmentDocBuilder> { custom };
        var builder = new SqlDocBuilder(registered);
        registered.Clear();

        Assert.Equal("[stmt]", _renderer.Render(builder.BuildDocument(result)));
    }

    [Fact]
    public void BuildDocument_CanBeCanceled()
    {
        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => new SqlDocBuilder().BuildDocument(result, cancellation.Token));
    }

    [Fact]
    public void BuilderRejectsNullArgumentsAndNullBuilderResults()
    {
        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        var builder = new SqlDocBuilder();

        Assert.Throws<ArgumentException>(() => new SqlDocBuilder(new ISqlFragmentDocBuilder[] { null! }));
        Assert.Throws<ArgumentNullException>(() => builder.BuildDocument(null!));
        Assert.Throws<ArgumentNullException>(() => builder.BuildFragment(null!, result.Root!));
        Assert.Throws<ArgumentNullException>(() => builder.BuildFragment(result, null!));
        Assert.Throws<InvalidOperationException>(() =>
            new SqlDocBuilder(new ISqlFragmentDocBuilder[] { new NullSelectBuilder() }).BuildDocument(result));
    }

    [Fact]
    public void FragmentWalker_VisitsScriptAndStatements()
    {
        var result = _parser.Parse("SELECT 1; SELECT 2;", SqlDialectVersion.Auto);
        var walker = new SqlFragmentWalker();

        var nodes = walker.Walk(result.Root!);

        Assert.Contains(nodes, node => node is TSqlScript);
        Assert.Equal(2, nodes.Count(node => node is SelectStatement));
    }

    [Fact]
    public void FragmentWalker_RejectsNullAndCanceledRequests()
    {
        var result = _parser.Parse("SELECT 1;", SqlDialectVersion.Auto);
        var walker = new SqlFragmentWalker();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<ArgumentNullException>(() => walker.Walk(null!));
        Assert.Throws<OperationCanceledException>(() => walker.Walk(result.Root!, cancellation.Token));
    }

    private sealed class ReplacingSelectBuilder : ISqlFragmentDocBuilder
    {
        private readonly string _replacement;

        public ReplacingSelectBuilder(string replacement = "[stmt]")
        {
            _replacement = replacement;
        }

        public int BuildCount { get; private set; }

        public bool CanBuild(TSqlFragment fragment) => fragment is SelectStatement;

        public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
        {
            BuildCount++;
            return new TextDoc(_replacement);
        }
    }

    private sealed class NullSelectBuilder : ISqlFragmentDocBuilder
    {
        public bool CanBuild(TSqlFragment fragment) => fragment is SelectStatement;

        public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context) => null!;
    }
}
