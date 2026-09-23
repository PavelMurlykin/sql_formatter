using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Parsing;

public sealed class SqlParseResult
{
    public SqlParseResult(
        string source,
        SqlDialectVersion requestedDialect,
        SqlVersion parserVersion,
        TSqlFragment? root,
        IReadOnlyList<TSqlParserToken> tokens,
        IReadOnlyList<ParseDiagnostic> diagnostics)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        LineMap = new SqlLineMap(Source);
        RequestedDialect = requestedDialect;
        ParserVersion = parserVersion;
        Root = root;
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public string Source { get; }

    public SqlLineMap LineMap { get; }

    public SqlDialectVersion RequestedDialect { get; }

    public SqlVersion ParserVersion { get; }

    public TSqlFragment? Root { get; }

    public IReadOnlyList<TSqlParserToken> Tokens { get; }

    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; }

    public bool ParseSucceeded => Root is not null && Diagnostics.Count == 0;
}
