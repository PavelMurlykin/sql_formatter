using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

internal static class TriggerLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("trigger.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var lists = new SqlListLayout(parsed, options, editor);
        foreach (var trigger in Triggers(parsed.Root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (trigger.TriggerObject is not { } target || trigger.TriggerActions.Count == 0) continue;
            var on = editor.Find(End(trigger.Name), End(target), Is("ON"));
            if (on is not null)
            {
                Boundary(on.Offset, "on.breakBefore", "on.keywordIndent", trigger.StartOffset);
                var first = editor.Find(on.Offset + on.Text.Length, End(target), _ => true);
                if (first is not null) Boundary(first.Offset, "on.breakAfter", "on.targetIndent", on.Offset);
            }
            if (trigger.Options.Count > 0)
            {
                var items = trigger.Options.Cast<TSqlFragment>().ToArray();
                var with = editor.Find(End(target), items[0].StartOffset, Is("WITH"));
                if (with is not null)
                {
                    Boundary(with.Offset, "with.breakBefore", "with.keywordIndent", trigger.StartOffset);
                    Boundary(items[0].StartOffset, "with.breakAfter", "with.listIndent", with.Offset);
                    lists.Apply(items, "trigger.with", with.Offset, with.Offset);
                }
            }
            var events = trigger.TriggerActions.Cast<TSqlFragment>().ToArray();
            var type = editor.Find(End(target), events[0].StartOffset,
                token => Is("FOR")(token) || Is("AFTER")(token) || Is("INSTEAD")(token));
            if (type is not null)
            {
                Boundary(type.Offset, "events.breakBefore", "events.keywordIndent", trigger.StartOffset);
                Boundary(events[0].StartOffset, "events.breakAfter", "events.listIndent", type.Offset);
                lists.Apply(events, "trigger.events", type.Offset, type.Offset);
            }
            var firstBody = trigger.StatementList?.Statements.FirstOrDefault();
            var start = firstBody?.StartOffset ?? trigger.MethodSpecifier?.StartOffset ?? End(trigger);
            var asKeyword = editor.FindLast(End(events[events.Length - 1]), start, Is("AS"));
            if (asKeyword is not null) Boundary(asKeyword.Offset, "body.breakBeforeAs", "body.asIndent", trigger.StartOffset);
            if (firstBody is not null)
            {
                editor.BeforeFragment(firstBody.StartOffset, Choice("body.breakAfterAs"), asKeyword?.Offset ?? trigger.StartOffset);
                if (firstBody is BeginEndBlockStatement { StatementList.Statements.Count: > 0 } block)
                {
                    editor.Before(block.StartOffset, "inherit", Indent("body.keywordIndent"), anchorOffset: asKeyword?.Offset ?? trigger.StartOffset);
                    var end = editor.FindLast(End(block.StatementList.Statements[block.StatementList.Statements.Count - 1]), End(block), Is("END"));
                    if (end is not null) editor.Before(end.Offset, "inherit", Indent("body.keywordIndent"), anchorOffset: asKeyword?.Offset ?? trigger.StartOffset);
                }
            }
        }
        var changed = editor.Apply(cancellationToken);
        if (changed != source)
        {
            var after = parser.Parse(changed, dialect, cancellationToken);
            if (!after.ParseSucceeded || !SqlSpacing.SameTokens(parsed, after)) return source;
        }
        return SqlFragmentLayout.IndentTreeSafe(changed, options, parser, dialect, cancellationToken, root =>
            Triggers(root).Where(t => t.StatementList?.Statements.Count > 0).Select(trigger =>
            {
                var body = trigger.StatementList.Statements[0];
                var asKeyword = root.ScriptTokenStream.LastOrDefault(t => t.Offset >= trigger.StartOffset && t.Offset < body.StartOffset && Is("AS")(t));
                return body is BeginEndBlockStatement { StatementList.Statements.Count: > 0 } block
                    ? new FragmentIndent(block.StatementList, block.StartOffset, Indent("body.codeIndent"))
                    : new FragmentIndent(trigger.StatementList, asKeyword?.Offset ?? trigger.StartOffset, Indent("body.codeIndent"));
            }));

        IEnumerable<TriggerStatementBody> Triggers(TSqlFragment root) => new SqlFragmentWalker().Walk(root, cancellationToken).OfType<TriggerStatementBody>();
        void Boundary(int offset, string key, string indent, int anchor) => editor.Before(offset, Choice(key), Indent(indent), anchorOffset: anchor);
        string Choice(string key) => NativeRules.Get(options, "trigger." + key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, "trigger." + key).Indent;
    }
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
