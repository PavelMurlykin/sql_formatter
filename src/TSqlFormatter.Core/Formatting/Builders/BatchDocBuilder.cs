using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

internal sealed class BatchDocBuilder : ISqlFragmentDocBuilder
{
    public bool CanBuild(TSqlFragment fragment) => fragment is TSqlBatch;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        var batch = (TSqlBatch)fragment;
        return context.StitchFragment(batch, batch.Statements);
    }
}
