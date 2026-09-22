using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Parsing;

internal static class SqlParserVersionMap
{
    public static SqlVersion ToScriptDomVersion(SqlDialectVersion dialect) => dialect switch
    {
        SqlDialectVersion.Auto => SqlVersion.Sql180,
        SqlDialectVersion.Sql2016 => SqlVersion.Sql130,
        SqlDialectVersion.Sql2017 => SqlVersion.Sql140,
        SqlDialectVersion.Sql2019 => SqlVersion.Sql150,
        SqlDialectVersion.Sql2022 => SqlVersion.Sql160,
        SqlDialectVersion.Latest => SqlVersion.Sql180,
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unknown SQL dialect version.")
    };

    public static TSqlParser CreateParser(SqlVersion version, bool initialQuotedIdentifiers) => version switch
    {
        SqlVersion.Sql130 => new TSql130Parser(initialQuotedIdentifiers),
        SqlVersion.Sql140 => new TSql140Parser(initialQuotedIdentifiers),
        SqlVersion.Sql150 => new TSql150Parser(initialQuotedIdentifiers),
        SqlVersion.Sql160 => new TSql160Parser(initialQuotedIdentifiers),
        SqlVersion.Sql180 => new TSql180Parser(initialQuotedIdentifiers),
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported ScriptDom parser version.")
    };
}
