using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

internal static class DeclareLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("declare.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var lists = new SqlListLayout(parsed, options, editor);
        foreach (var node in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is DeclareVariableStatement { Declarations.Count: > 0 } variables)
            {
                var items = variables.Declarations.Cast<TSqlFragment>().ToArray();
                Boundary(items[0].StartOffset, "variables.breakAfter", "variables.listIndent", node.StartOffset);
                if (lists.Apply(items, "declare.variables", node.StartOffset, node.StartOffset))
                    foreach (var item in variables.Declarations)
                        editor.Before(item.DataType.StartOffset, "inherit", spaceMode: "insert");
            }
            else if (node is DeclareTableVariableStatement { Body: { VariableName: { } name, Definition: { } definition } })
            {
                Boundary(name.StartOffset, "variables.breakAfter", "variables.listIndent", node.StartOffset);
                var table = editor.Find(End(name), definition.StartOffset, Is("TABLE"));
                if (table is not null) Boundary(table.Offset, "variables.breakBeforeTable", "variables.tableIndent", node.StartOffset);
            }
            else if (node is DeclareCursorStatement { CursorDefinition: { Select: { } query } } cursor)
            {
                var keyword = editor.Find(End(cursor.Name), query.StartOffset, Is("CURSOR"));
                var forKeyword = editor.Find(End(cursor.Name), query.StartOffset, Is("FOR"));
                if (keyword is not null) Boundary(keyword.Offset, "cursor.breakBefore", "cursor.keywordIndent", node.StartOffset);
                if (forKeyword is not null) Boundary(forKeyword.Offset, "cursor.breakBeforeFor", "cursor.forIndent", node.StartOffset);
                editor.BeforeFragment(query.StartOffset, Choice("cursor.breakBeforeQuery"), node.StartOffset);
            }
        }
        var changed = editor.Apply(cancellationToken);
        if (changed != source)
        {
            var after = parser.Parse(changed, dialect, cancellationToken);
            if (!after.ParseSucceeded || !SqlSpacing.SameTokens(parsed, after)) return source;
        }
        changed = SqlFragmentLayout.IndentSafe(changed, options, parser, dialect, cancellationToken,
            root => Cursors(root).Select(c => new FragmentIndent(c.CursorDefinition.Select, c.StartOffset, Indent("cursor.queryIndent"))));
        return SqlFragmentLayout.CompactSafe(changed, options, parser, dialect, cancellationToken,
            "declare.cursor.singleLine", root => Cursors(root).Select(c => (TSqlFragment)c.CursorDefinition.Select));

        IEnumerable<DeclareCursorStatement> Cursors(TSqlFragment root) => new SqlFragmentWalker().Walk(root, cancellationToken)
            .OfType<DeclareCursorStatement>().Where(c => c.CursorDefinition?.Select is not null);
        void Boundary(int offset, string key, string indent, int anchor) => editor.Before(offset, Choice(key), Indent(indent), anchorOffset: anchor);
        string Choice(string key) => NativeRules.Get(options, "declare." + key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, "declare." + key).Indent;
    }
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
