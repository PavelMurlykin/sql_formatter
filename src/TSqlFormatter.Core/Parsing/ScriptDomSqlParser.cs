using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Parsing;

public sealed class ScriptDomSqlParser : ISqlParser
{
    private readonly bool _initialQuotedIdentifiers;

    public ScriptDomSqlParser(bool initialQuotedIdentifiers = true)
    {
        _initialQuotedIdentifiers = initialQuotedIdentifiers;
    }

    public SqlParseResult Parse(
        string source,
        SqlDialectVersion dialect,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        cancellationToken.ThrowIfCancellationRequested();

        var parserVersion = SqlParserVersionMap.ToScriptDomVersion(dialect);
        var parser = SqlParserVersionMap.CreateParser(parserVersion, _initialQuotedIdentifiers);
        using var reader = new StringReader(source);
        TSqlFragment? root = parser.Parse(reader, out var errors);

        var tokenStream = root?.ScriptTokenStream;
        if (tokenStream is null)
        {
            using var tokenReader = new StringReader(source);
            tokenStream = parser.GetTokenStream(tokenReader, out _);
        }

        var tokens = new TSqlParserToken[tokenStream.Count];
        tokenStream.CopyTo(tokens, 0);

        var diagnostics = new ParseDiagnostic[errors.Count];
        for (var index = 0; index < errors.Count; index++)
        {
            var error = errors[index];
            diagnostics[index] = new ParseDiagnostic(
                error.Number,
                error.Message,
                error.Offset,
                error.Line,
                error.Column);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new SqlParseResult(
            source,
            dialect,
            parserVersion,
            root,
            Array.AsReadOnly(tokens),
            Array.AsReadOnly(diagnostics));
    }
}
