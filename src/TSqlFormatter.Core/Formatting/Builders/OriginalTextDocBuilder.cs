using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

internal sealed class OriginalTextDocBuilder : ISqlFragmentDocBuilder
{
    public bool CanBuild(TSqlFragment fragment) => true;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        return new TextDoc(context.GetOriginalText(fragment));
    }
}
