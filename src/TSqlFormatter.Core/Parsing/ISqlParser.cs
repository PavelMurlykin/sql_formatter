namespace TSqlFormatter.Core.Parsing;

public interface ISqlParser
{
    SqlParseResult Parse(
        string source,
        SqlDialectVersion dialect,
        CancellationToken cancellationToken = default);
}
