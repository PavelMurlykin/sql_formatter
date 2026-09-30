using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

internal static class ModuleLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("routine.", StringComparison.Ordinal)
            || key.StartsWith("view.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var lists = new SqlListLayout(parsed, options, editor);
        foreach (var node in Walk(parsed.Root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is ProcedureStatementBodyBase routine && Parts(routine) is { } parts)
            {
                var prefix = "routine.parameters";
                var items = routine.Parameters.Cast<TSqlFragment>().ToArray();
                var first = items.Length == 0 ? parts.HeaderEnd : items[0].StartOffset;
                var open = editor.Find(End(parts.Name), first, t => t.Text == "(");
                var close = open is null ? null : editor.Find(items.Length == 0 ? open.Offset + 1 : End(items[items.Length - 1]),
                    parts.HeaderEnd, t => t.Text == ")");
                if (open is not null && close is not null)
                {
                    editor.Before(open.Offset, Choice(prefix + ".breakBeforeOpen"), Indent(prefix + ".braceIndent"),
                        Choice(prefix + ".spaceBeforeOpen"), node.StartOffset);
                    if (items.Length > 0) Braced(items, open, close, prefix, node.StartOffset);
                    else editor.Before(close.Offset, Choice(prefix + ".breakBeforeClose"), Indent(prefix + ".braceIndent"),
                        Choice(prefix + ".spaceWithinEmpty"), node.StartOffset);
                }
                else if (items.Length > 0)
                {
                    Boundary(items[0].StartOffset, prefix + ".breakAfterOpen", prefix + ".listIndent", node.StartOffset);
                    lists.Apply(items, prefix, node.StartOffset, node.StartOffset, End(items[items.Length - 1]));
                }
                if (parts.Options.Length > 0)
                {
                    var with = editor.Find(End(parts.Name), parts.Options[0].StartOffset, Is("WITH"));
                    if (with is not null)
                    {
                        Boundary(with.Offset, "routine.with.breakBefore", "routine.with.keywordIndent", node.StartOffset);
                        Boundary(parts.Options[0].StartOffset, "routine.with.breakAfter", "routine.with.listIndent", with.Offset);
                        lists.Apply(parts.Options, "routine.with", with.Offset, with.Offset);
                    }
                }
                var asKeyword = editor.FindLast(parts.AsStart, parts.BodyStart, Is("AS"));
                if (asKeyword is not null)
                    Boundary(asKeyword.Offset, "routine.body.breakBeforeAs", "routine.body.asIndent", node.StartOffset);
                if (parts.Body is not null)
                {
                    editor.BeforeFragment(parts.BodyStart, Choice("routine.body.breakBefore"), asKeyword?.Offset ?? node.StartOffset);
                    if (parts.Body is BeginEndBlockStatement { StatementList.Statements.Count: > 0 } block)
                    {
                        editor.Before(block.StartOffset, "inherit", Indent("routine.body.keywordIndent"), anchorOffset: asKeyword?.Offset ?? node.StartOffset);
                        var end = editor.FindLast(End(block.StatementList.Statements[block.StatementList.Statements.Count - 1]), End(block), Is("END"));
                        if (end is not null) editor.Before(end.Offset, "inherit", Indent("routine.body.keywordIndent"), anchorOffset: asKeyword?.Offset ?? node.StartOffset);
                    }
                }
                if (routine is FunctionStatementBody { ReturnType: { } returns } function)
                {
                    var keyword = editor.Find(End(parts.Name), parts.BodyStart, Is("RETURNS"));
                    if (keyword is not null) editor.Before(keyword.Offset, Choice("routine.returns.breakBefore"), anchorOffset: node.StartOffset);
                    var table = TableReturn(function, editor);
                    if (table is not null) editor.BeforeFragment(table.Value.Start, Choice("routine.returns.breakBeforeTable"), keyword?.Offset ?? node.StartOffset);
                }
            }
            else if (node is ViewStatementBody { SelectStatement: { } query } view)
            {
                var items = view.Columns.Cast<TSqlFragment>().ToArray();
                if (items.Length > 0)
                {
                    var open = editor.Find(End(view.SchemaObjectName), items[0].StartOffset, t => t.Text == "(");
                    var close = editor.Find(End(items[items.Length - 1]), query.StartOffset, t => t.Text == ")");
                    if (open is not null && close is not null)
                    {
                        editor.Before(open.Offset, Choice("view.columns.breakBeforeOpen"), Indent("view.columns.braceIndent"),
                            Choice("view.columns.spaceBeforeOpen"), node.StartOffset);
                        Braced(items, open, close, "view.columns", node.StartOffset);
                    }
                }
                var asKeyword = editor.FindLast(End(view.SchemaObjectName), query.StartOffset, Is("AS"));
                if (asKeyword is not null) Boundary(asKeyword.Offset, "view.query.breakBeforeAs", "view.query.asIndent", node.StartOffset);
                editor.BeforeFragment(query.StartOffset, Choice("view.query.breakAfterAs"), asKeyword?.Offset ?? node.StartOffset);
            }
        }
        var changed = editor.Apply(cancellationToken);
        if (changed != source)
        {
            var after = parser.Parse(changed, dialect, cancellationToken);
            if (!after.ParseSucceeded || !SqlSpacing.SameTokens(parsed, after)) return source;
        }
        changed = SqlFragmentLayout.IndentTreeSafe(changed, options, parser, dialect, cancellationToken, Indents);
        return SqlFragmentLayout.CompactSafe(changed, options, parser, dialect, cancellationToken,
            "view.query.singleLine", root => Walk(root).OfType<ViewStatementBody>().Where(v => v.SelectStatement is not null)
                .Select(v => (TSqlFragment)v.SelectStatement));

        IEnumerable<FragmentIndent> Indents(TSqlFragment root)
        {
            var tokenSource = string.Concat(root.ScriptTokenStream.Select(t => t.Text));
            var tokens = new SqlTokenGapEditor(new SqlParseResult(tokenSource, dialect, parsed.ParserVersion,
                root, root.ScriptTokenStream.ToArray(), Array.Empty<ParseDiagnostic>()), options);
            foreach (var node in Walk(root))
            {
                if (node is ProcedureStatementBodyBase routine && Parts(routine) is { } parts)
                {
                    var asKeyword = tokens.FindLast(parts.AsStart, parts.BodyStart, Is("AS"));
                    if (parts.Body is BeginEndBlockStatement { StatementList.Statements.Count: > 0 } block)
                        yield return new FragmentIndent(block.StatementList, block.StartOffset, Indent("routine.body.codeIndent"));
                    else if (routine.StatementList?.Statements.Count > 0)
                        yield return new FragmentIndent(routine.StatementList, asKeyword?.Offset ?? node.StartOffset, Indent("routine.body.codeIndent"));
                    else if (parts.Body is { } body)
                        yield return new FragmentIndent(body, asKeyword?.Offset ?? node.StartOffset, Indent("routine.body.codeIndent"),
                            End(body), parts.BodyStart);
                    if (routine is FunctionStatementBody function && TableReturn(function, tokens) is { } table)
                        yield return new FragmentIndent(function.ReturnType, node.StartOffset, Indent("routine.returns.tableIndent"), table.End, table.Start);
                }
                else if (node is ViewStatementBody { SelectStatement: { } query } view)
                {
                    var asKeyword = tokens.FindLast(End(view.SchemaObjectName), query.StartOffset, Is("AS"));
                    yield return new FragmentIndent(query, asKeyword?.Offset ?? node.StartOffset, Indent("view.query.queryIndent"));
                }
            }
        }
        IReadOnlyList<TSqlFragment> Walk(TSqlFragment root) => new SqlFragmentWalker().Walk(root, cancellationToken);
        void Braced(TSqlFragment[] items, TSqlParserToken open, TSqlParserToken close, string prefix, int anchor)
        {
            editor.Before(items[0].StartOffset, Choice(prefix + ".breakAfterOpen"), Indent(prefix + ".listIndent"), Choice(prefix + ".spaceWithin"), open.Offset);
            lists.Apply(items, prefix, open.Offset, open.Offset, close.Offset + close.Text.Length);
            editor.Before(close.Offset, Choice(prefix + ".breakBeforeClose"), Indent(prefix + ".braceIndent"), Choice(prefix + ".spaceWithin"), anchor);
        }
        void Boundary(int offset, string key, string indent, int anchor) => editor.Before(offset, Choice(key), Indent(indent), anchorOffset: anchor);
        string Choice(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, key).Indent;
    }

    private sealed class RoutineParts
    {
        public RoutineParts(TSqlFragment name, TSqlFragment[] options, TSqlFragment? body, int start, int headerEnd, int asStart)
        { Name = name; Options = options; Body = body; BodyStart = start; HeaderEnd = headerEnd; AsStart = asStart; }
        public TSqlFragment Name { get; }
        public TSqlFragment[] Options { get; }
        public TSqlFragment? Body { get; }
        public int BodyStart { get; }
        public int HeaderEnd { get; }
        public int AsStart { get; }
    }
    private static RoutineParts? Parts(ProcedureStatementBodyBase routine)
    {
        TSqlFragment? name = routine switch { FunctionStatementBody f => f.Name, ProcedureStatementBody p => p.ProcedureReference, _ => null };
        if (name is null) return null;
        var options = routine switch { FunctionStatementBody f => f.Options.Cast<TSqlFragment>().ToArray(),
            ProcedureStatementBody p => p.Options.Cast<TSqlFragment>().ToArray(), _ => Array.Empty<TSqlFragment>() };
        var body = routine.StatementList?.Statements.FirstOrDefault();
        if (body is null && routine is FunctionStatementBody { ReturnType: SelectFunctionReturnType { SelectStatement: { } query } }) body = query;
        var start = body?.StartOffset ?? routine.MethodSpecifier?.StartOffset ?? End(routine);
        if (body is SelectStatement)
            start = routine.ScriptTokenStream.Where(t => t.Offset >= End(name) && t.Offset < start)
                .LastOrDefault(Is("RETURN"))?.Offset ?? start;
        var headerEnd = new[] { options.FirstOrDefault()?.StartOffset ?? start,
            routine is FunctionStatementBody headerFunction ? headerFunction.ReturnType.StartOffset : start, start }.Min();
        var asStart = Math.Max(End(name), Math.Max(routine.Parameters.LastOrDefault() is { } parameter ? End(parameter) : 0,
            options.LastOrDefault() is { } option ? End(option) : 0));
        if (routine is FunctionStatementBody function)
            asStart = Math.Max(asStart, function.ReturnType is SelectFunctionReturnType
                ? function.ScriptTokenStream.FirstOrDefault(t => t.Offset >= function.ReturnType.StartOffset && t.Offset < start && Is("TABLE")(t)) is { } table
                    ? table.Offset + table.Text.Length : asStart
                : End(function.ReturnType));
        return new RoutineParts(name, options, body, start, headerEnd, asStart);
    }
    private static (int Start, int End)? TableReturn(FunctionStatementBody function, SqlTokenGapEditor editor)
    {
        var returnType = function.ReturnType;
        if (returnType is not (TableValuedFunctionReturnType or SelectFunctionReturnType)) return null;
        var end = returnType is SelectFunctionReturnType { SelectStatement: { } select } ? select.StartOffset : End(returnType);
        var table = editor.Find(returnType.StartOffset, end, Is("TABLE"));
        if (table is null) return null;
        return (table.Offset, returnType is SelectFunctionReturnType ? table.Offset + table.Text.Length : end);
    }
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
