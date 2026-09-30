using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

internal static class ExecuteLabelLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("execute.", StringComparison.Ordinal)
            || key.StartsWith("labels.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var lists = new SqlListLayout(parsed, options, editor);
        var nodes = new SqlFragmentWalker().Walk(parsed.Root, cancellationToken);
        foreach (var statement in nodes.OfType<ExecuteStatement>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement.ExecuteSpecification?.ExecutableEntity is not { Parameters.Count: > 0 } entity) continue;
            var items = entity.Parameters.Cast<TSqlFragment>().ToArray();
            editor.Before(items[0].StartOffset, Choice("execute.parameters.breakBefore"), Indent("execute.parameters.listIndent"), anchorOffset: statement.StartOffset);
            lists.Apply(items, "execute.parameters", statement.StartOffset, statement.StartOffset, End(items[items.Length - 1]));
        }
        foreach (var list in nodes.SelectMany(node => node switch
                 { TSqlBatch batch => new[] { batch.Statements }, StatementList list => new[] { list.Statements }, _ => Array.Empty<IList<TSqlStatement>>() }))
            for (var index = 0; index < list.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (list[index] is not LabelStatement label) continue;
                var anchor = nodes.OfType<BeginEndBlockStatement>().Where(block => block.StartOffset < label.StartOffset && End(block) > End(label))
                    .OrderBy(block => block.FragmentLength).FirstOrDefault()?.StartOffset ?? 0;
                editor.IndentAtBoundary(label.StartOffset, Indent("labels.indent"), anchor);
                var following = index + 1 < list.Count ? list[index + 1] : null;
                if (following is not null) editor.BeforeFragment(following.StartOffset, Choice("labels.breakAfter"), label.StartOffset);
                if (NativeRules.Get(options, "labels.blankLinesAround").Boolean)
                {
                    editor.BlankBefore(label.StartOffset);
                    if (following is not null && index + 2 < list.Count) editor.BlankBefore(list[index + 2].StartOffset);
                }
            }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var after = parser.Parse(changed, dialect, cancellationToken);
        return after.ParseSucceeded && SqlSpacing.SameTokens(parsed, after) ? changed : source;
        string Choice(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, key).Indent;
    }
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
}
