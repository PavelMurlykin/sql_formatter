using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Tests;

public sealed class ScriptDomSmokeTests
{
    [Fact]
    public void Parse_SelectLiteral_ReturnsOneSelectStatementWithoutErrors()
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader("SELECT 1;");

        var fragment = parser.Parse(reader, out var errors);

        Assert.Empty(errors);
        var script = Assert.IsType<TSqlScript>(fragment);
        var batch = Assert.Single(script.Batches);
        Assert.IsType<SelectStatement>(Assert.Single(batch.Statements));
    }
}
