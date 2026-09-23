using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>A focused AST-node-to-Doc strategy. Earlier registered builders take precedence.</summary>
public interface ISqlFragmentDocBuilder
{
    bool CanBuild(TSqlFragment fragment);

    Doc Build(TSqlFragment fragment, SqlDocBuilderContext context);
}
