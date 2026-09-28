using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Formatting.Builders;

internal static class OrderByClauseCompatibility
{
    // SSMS 22.10.1 loads ScriptDom 18.0.56.2, which predates this optional property.
    // Resolve it late so Core can run against both that host copy and NuGet 180.107.0.
    public static bool IsAll(OrderByClause clause) =>
        clause.GetType().GetProperty("All")?.GetValue(clause) is true;
}
