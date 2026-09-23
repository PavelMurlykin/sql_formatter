using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Source positions and recursive dispatch for focused fragment builders.</summary>
public sealed class SqlDocBuilderContext
{
    private readonly SqlDocBuilder _owner;
    private readonly SqlTokenNavigator _navigator;

    internal SqlDocBuilderContext(SqlDocBuilder owner, SqlParseResult parseResult, CancellationToken cancellationToken)
    {
        _owner = owner;
        ParseResult = parseResult;
        CancellationToken = cancellationToken;
        _navigator = new SqlTokenNavigator(parseResult);
    }

    public SqlParseResult ParseResult { get; }

    public CancellationToken CancellationToken { get; }

    public Doc BuildFragment(TSqlFragment fragment)
    {
        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        return _owner.BuildNode(fragment, this);
    }

    public string GetOriginalText(TSqlFragment fragment)
    {
        var span = _navigator.GetTextSpan(fragment);
        return ParseResult.Source.Substring(span.StartOffset, span.Length);
    }

    public Doc StitchSource<TFragment>(IEnumerable<TFragment> children)
        where TFragment : TSqlFragment
    {
        return Stitch(new SqlTextSpan(0, ParseResult.Source.Length), children);
    }

    public Doc StitchFragment<TFragment>(TSqlFragment parent, IEnumerable<TFragment> children)
        where TFragment : TSqlFragment
    {
        if (parent is null)
        {
            throw new ArgumentNullException(nameof(parent));
        }

        return Stitch(_navigator.GetTextSpan(parent), children);
    }

    private Doc Stitch<TFragment>(SqlTextSpan parentSpan, IEnumerable<TFragment> children)
        where TFragment : TSqlFragment
    {
        if (children is null)
        {
            throw new ArgumentNullException(nameof(children));
        }

        var parts = new List<Doc>();
        var cursor = parentSpan.StartOffset;
        foreach (var child in children)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (child is null)
            {
                throw new ArgumentException("Children cannot contain null fragments.", nameof(children));
            }

            var childSpan = _navigator.GetTextSpan(child);
            if (childSpan.StartOffset < cursor || childSpan.EndOffset > parentSpan.EndOffset)
            {
                throw new ArgumentException("Child fragments must be ordered, nonoverlapping, and inside the parent span.", nameof(children));
            }

            if (childSpan.StartOffset > cursor)
            {
                parts.Add(new TextDoc(ParseResult.Source.Substring(cursor, childSpan.StartOffset - cursor)));
            }

            parts.Add(BuildFragment(child));
            cursor = childSpan.EndOffset;
        }

        if (cursor < parentSpan.EndOffset)
        {
            parts.Add(new TextDoc(ParseResult.Source.Substring(cursor, parentSpan.EndOffset - cursor)));
        }

        return parts.Count switch
        {
            0 => new TextDoc(string.Empty),
            1 => parts[0],
            _ => new ConcatDoc(parts)
        };
    }
}
