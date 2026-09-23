using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Dispatches AST fragments to focused builders while preserving unsupported source.</summary>
public sealed class SqlDocBuilder
{
    private readonly IReadOnlyList<ISqlFragmentDocBuilder> _builders;

    public SqlDocBuilder(IReadOnlyList<ISqlFragmentDocBuilder>? builders = null)
    {
        var customCount = builders?.Count ?? 0;
        var all = new ISqlFragmentDocBuilder[customCount + 3];
        for (var index = 0; index < customCount; index++)
        {
            all[index] = builders![index]
                ?? throw new ArgumentException("Builders cannot contain null entries.", nameof(builders));
        }

        all[customCount] = new ScriptDocBuilder();
        all[customCount + 1] = new BatchDocBuilder();
        all[customCount + 2] = new OriginalTextDocBuilder();
        _builders = Array.AsReadOnly(all);
    }

    /// <summary>Builds a source-preserving document; parse failures remain unchanged.</summary>
    public Doc BuildDocument(SqlParseResult parseResult, CancellationToken cancellationToken = default)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!parseResult.ParseSucceeded)
        {
            return new TextDoc(parseResult.Source);
        }

        return BuildFragment(parseResult, parseResult.Root!, cancellationToken);
    }

    public Doc BuildFragment(
        SqlParseResult parseResult,
        TSqlFragment fragment,
        CancellationToken cancellationToken = default)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        var context = new SqlDocBuilderContext(this, parseResult, cancellationToken);
        return BuildNode(fragment, context);
    }

    internal Doc BuildNode(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        foreach (var builder in _builders)
        {
            if (builder.CanBuild(fragment))
            {
                return builder.Build(fragment, context)
                    ?? throw new InvalidOperationException("A fragment builder returned null.");
            }
        }

        throw new InvalidOperationException("No fragment builder accepted the AST node.");
    }
}
