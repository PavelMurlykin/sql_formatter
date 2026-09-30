using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

internal static class CodeLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("code.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var nodes = Walk(parsed.Root);
        var owners = Owners(nodes);
        foreach (var list in Lists(nodes))
            foreach (var statement in list.Skip(1))
                editor.BeforeFragment(statement.StartOffset, Choice("separateStatements"), list[0].StartOffset);
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is IfStatement { Predicate: { } predicate, ThenStatement: { } then } conditional)
            {
                Condition(predicate, then, "if", node.StartOffset);
                if (conditional.ElseStatement is { } alternative)
                {
                    var keyword = editor.Find(End(then), alternative.StartOffset, Is("ELSE"));
                    if (keyword is not null)
                    {
                        editor.Before(keyword.Offset, Choice("if.breakBeforeElse"), anchorOffset: node.StartOffset);
                        editor.BeforeFragment(alternative.StartOffset, Choice("if.breakAfterElse"), keyword.Offset);
                    }
                }
                Around(node, "if");
            }
            else if (node is WhileStatement { Predicate: { } condition, Statement: { } body })
            { Condition(condition, body, "while", node.StartOffset); Around(node, "while"); }
            else if (node is BeginEndBlockStatement { StatementList.Statements.Count: > 0 } block)
            {
                var statements = block.StatementList.Statements;
                editor.BeforeFragment(statements[0].StartOffset, Choice("breakAfterBegin"), node.StartOffset);
                var end = editor.FindLast(End(statements[statements.Count - 1]), End(block), Is("END"));
                if (owners.TryGetValue(block, out var owner))
                {
                    editor.Before(block.StartOffset, "inherit", Indent(owner.Prefix + ".keywordIndent"), anchorOffset: owner.Anchor);
                    if (end is not null) editor.Before(end.Offset, Choice("breakBeforeEnd"), Indent(owner.Prefix + ".keywordIndent"), anchorOffset: owner.Anchor);
                }
                else
                {
                    if (end is not null) editor.Before(end.Offset, Choice("breakBeforeEnd"), anchorOffset: block.StartOffset);
                    Around(node, "block");
                }
            }
            else if (node is TryCatchStatement { TryStatements.Statements.Count: > 0, CatchStatements.Statements.Count: > 0 } handler)
            {
                var tries = handler.TryStatements.Statements;
                var catches = handler.CatchStatements.Statements;
                var catchBegin = editor.Find(End(tries[tries.Count - 1]), catches[0].StartOffset, Is("BEGIN"));
                if (catchBegin is not null)
                    editor.Before(catchBegin.Offset, Choice("block.breakBeforeCatch"), anchorOffset: node.StartOffset);
                foreach (var list in new[] { tries, catches })
                {
                    editor.BeforeFragment(list[0].StartOffset, Choice("breakAfterBegin"), node.StartOffset);
                    var end = editor.Find(End(list[list.Count - 1]), End(handler), Is("END"));
                    if (end is not null) editor.Before(end.Offset, Choice("breakBeforeEnd"), anchorOffset: node.StartOffset);
                }
                Around(node, "block");
            }
        }
        var changed = editor.Apply(cancellationToken);
        if (changed != source)
        {
            var after = parser.Parse(changed, dialect, cancellationToken);
            if (!after.ParseSucceeded || !SqlSpacing.SameTokens(parsed, after)) return source;
        }
        return SqlFragmentLayout.IndentTreeSafe(changed, options, parser, dialect, cancellationToken, Indents);

        IEnumerable<FragmentIndent> Indents(TSqlFragment root)
        {
            var all = Walk(root);
            var owned = Owners(all);
            foreach (var node in all)
            {
                if (node is BeginEndBlockStatement { StatementList.Statements.Count: > 0 } block)
                {
                    var prefix = owned.TryGetValue(block, out var owner) ? owner.Prefix : "block";
                    yield return new FragmentIndent(block.StatementList, block.StartOffset, Indent(prefix + ".bodyIndent"));
                }
                else if (node is IfStatement conditional)
                {
                    if (conditional.ThenStatement is { } then && then is not BeginEndBlockStatement)
                        yield return new FragmentIndent(then, node.StartOffset, Indent("if.bodyIndent"));
                    if (conditional.ElseStatement is { } alternative && alternative is not BeginEndBlockStatement)
                        yield return new FragmentIndent(alternative, node.StartOffset, Indent("if.bodyIndent"));
                }
                else if (node is WhileStatement { Statement: { } body } && body is not BeginEndBlockStatement)
                    yield return new FragmentIndent(body, node.StartOffset, Indent("while.bodyIndent"));
                else if (node is TryCatchStatement handler)
                {
                    if (handler.TryStatements?.Statements.Count > 0)
                        yield return new FragmentIndent(handler.TryStatements, node.StartOffset, Indent("block.bodyIndent"));
                    if (handler.CatchStatements?.Statements.Count > 0)
                        yield return new FragmentIndent(handler.CatchStatements, node.StartOffset, Indent("block.bodyIndent"));
                }
            }
            foreach (var list in Lists(all))
            {
                var begins = new Stack<int>();
                var pairs = new List<(int Start, int End)>();
                for (var index = 0; index < list.Count; index++)
                    if (list[index] is BeginTransactionStatement) begins.Push(index);
                    else if (list[index] is CommitTransactionStatement or RollbackTransactionStatement && begins.Count > 0)
                        pairs.Add((begins.Pop(), index));
                foreach (var pair in pairs.OrderBy(p => p.Start))
                    if (pair.End > pair.Start + 1)
                        yield return new FragmentIndent(list[pair.Start + 1], list[pair.Start].StartOffset,
                            Indent("transaction.bodyIndent"), End(list[pair.End - 1]));
            }
        }
        IReadOnlyList<TSqlFragment> Walk(TSqlFragment root) => new SqlFragmentWalker().Walk(root, cancellationToken);
        void Condition(BooleanExpression predicate, TSqlStatement body, string prefix, int anchor)
        {
            editor.Before(predicate.StartOffset, "inherit", Indent(prefix + ".conditionIndent"), anchorOffset: anchor);
            SqlBooleanPolicy.Apply(predicate, editor, options, "code." + prefix, anchor);
            editor.BeforeFragment(body.StartOffset, Choice(prefix + ".breakAfterCondition"), anchor);
        }
        void Around(TSqlFragment node, string prefix)
        {
            if (!NativeRules.Get(options, "code." + prefix + ".blankLinesAround").Boolean) return;
            editor.BlankBefore(node.StartOffset);
            var next = editor.Find(End(node), source.Length, _ => true);
            if (next is not null && !Is("END")(next) && !Is("ELSE")(next) && !Is("GO")(next)) editor.BlankBefore(next.Offset);
        }
        string Choice(string key) => NativeRules.Get(options, "code." + key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, "code." + key).Indent;
    }

    private static Dictionary<BeginEndBlockStatement, (string Prefix, int Anchor)> Owners(IEnumerable<TSqlFragment> nodes)
    {
        var owners = new Dictionary<BeginEndBlockStatement, (string, int)>();
        foreach (var node in nodes)
        {
            if (node is IfStatement conditional)
                foreach (var branch in new[] { conditional.ThenStatement, conditional.ElseStatement }.OfType<BeginEndBlockStatement>())
                    owners[branch] = ("if", node.StartOffset);
            else if (node is WhileStatement { Statement: BeginEndBlockStatement block }) owners[block] = ("while", node.StartOffset);
        }
        return owners;
    }
    private static IEnumerable<IList<TSqlStatement>> Lists(IEnumerable<TSqlFragment> nodes) => nodes.SelectMany(node => node switch
    {
        TSqlBatch batch => new[] { batch.Statements },
        StatementList list => new[] { list.Statements },
        _ => Array.Empty<IList<TSqlStatement>>()
    });
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
