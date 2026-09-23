using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

public sealed class FormatRequest
{
    public FormatRequest(
        FormatScope scope = FormatScope.Document,
        SqlTextSpan? selection = null,
        SqlDialectVersion dialect = SqlDialectVersion.Auto,
        ParseFailureBehavior parseFailureBehavior = ParseFailureBehavior.Strict)
    {
        if (!Enum.IsDefined(typeof(FormatScope), scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if (scope == FormatScope.Selection && selection is null)
        {
            throw new ArgumentException("Selection scope requires a text span.", nameof(selection));
        }

        if (!Enum.IsDefined(typeof(SqlDialectVersion), dialect))
        {
            throw new ArgumentOutOfRangeException(nameof(dialect));
        }

        if (!Enum.IsDefined(typeof(ParseFailureBehavior), parseFailureBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(parseFailureBehavior));
        }

        Scope = scope;
        Selection = selection;
        Dialect = dialect;
        ParseFailureBehavior = parseFailureBehavior;
    }

    public FormatScope Scope { get; }

    public SqlTextSpan? Selection { get; }

    public SqlDialectVersion Dialect { get; }

    public ParseFailureBehavior ParseFailureBehavior { get; }
}
