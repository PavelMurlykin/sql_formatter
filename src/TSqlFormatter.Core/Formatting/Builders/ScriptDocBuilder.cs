using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

internal sealed class ScriptDocBuilder : ISqlFragmentDocBuilder
{
    public bool CanBuild(TSqlFragment fragment) => fragment is TSqlScript;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        var script = (TSqlScript)fragment;
        return context.StitchSource(script.Batches);
    }
}
