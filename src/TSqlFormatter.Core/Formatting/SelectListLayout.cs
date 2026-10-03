using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>SELECT-column token-gap layout for space-offset profiles, including stored and nested queries.</summary>
internal static class SelectListLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!SelectQueryScope.UsesSpaceOffsets(options)) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is not TSqlScript script) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var lists = new SqlListLayout(parsed, options, editor);
        foreach (var query in SelectQueryScope.Queries(script, options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.SelectElements.Count == 0) continue;
            var elements = query.SelectElements.Cast<TSqlFragment>().ToArray();
            editor.Before(elements[0].StartOffset, NativeRules.Get(options, "select.list.breakBeforeFirstColumn").Choice,
                NativeRules.Get(options, "select.list.indent").Indent, anchorOffset: query.StartOffset);
            lists.Apply(elements, "select.list", query.StartOffset, stackKey: "select.list.stackColumns", indentKey: "select.list.indent");
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var after = parser.Parse(changed, dialect, cancellationToken);
        return after.ParseSucceeded && SqlSpacing.SameTokens(parsed, after) ? changed : source;
    }
}
