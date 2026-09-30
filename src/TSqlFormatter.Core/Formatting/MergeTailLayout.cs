using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

internal static class MergeTailLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => new[] { "merge.top.", "merge.output.", "merge.option." }
                .Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var lists = new SqlListLayout(parsed, options, editor);
        foreach (var statement in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken).OfType<MergeStatement>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement.MergeSpecification is not { } spec) continue;
            var anchor = spec.StartOffset;
            if (spec.TopRowFilter is { Expression: { } expression } top)
            {
                var keyword = editor.Find(top.StartOffset, End(top), Is("TOP"));
                if (keyword is not null)
                {
                    Boundary(keyword.Offset, "merge.top.breakBefore", "merge.top.keywordIndent", anchor);
                    var open = editor.Find(keyword.Offset + keyword.Text.Length, End(expression), t => t.Text == "(");
                    var close = editor.FindLast(expression.StartOffset, End(top), t => t.Text == ")");
                    if (open is not null && close is not null)
                    {
                        editor.Before(open.Offset, Choice("merge.top.breakBeforeOpen"),
                            spaceMode: Choice("merge.top.spaceAfterKeyword"), anchorOffset: keyword.Offset);
                        editor.After(open.Offset, Choice("merge.top.breakAfterOpen"),
                            spaceMode: Choice("merge.top.spaceWithin"), anchorOffset: keyword.Offset);
                        editor.Before(close.Offset, Choice("merge.top.breakBeforeClose"),
                            spaceMode: Choice("merge.top.spaceWithin"), anchorOffset: keyword.Offset);
                    }
                    else editor.Before(expression.StartOffset, "inherit", spaceMode: Choice("merge.top.spaceAfterKeyword"));
                    var percent = editor.Find(End(expression), End(top), Is("PERCENT"));
                    if (percent is not null)
                        Boundary(percent.Offset, "merge.top.breakBeforePercent", "merge.top.percentIndent", keyword.Offset);
                }
            }
            if (spec.OutputIntoClause is { } into) Output(into, into.SelectColumns.Cast<TSqlFragment>().ToArray(), anchor);
            if (spec.OutputClause is { } output) Output(output, output.SelectColumns.Cast<TSqlFragment>().ToArray(), anchor);
            if (statement.OptimizerHints.Count > 0)
            {
                var first = statement.OptimizerHints[0];
                var keyword = editor.Find(End(spec), first.StartOffset, Is("OPTION"));
                var open = keyword is null ? null : editor.Find(keyword.Offset + keyword.Text.Length, first.StartOffset, t => t.Text == "(");
                if (keyword is not null && open is not null)
                {
                    Boundary(keyword.Offset, "merge.option.breakBefore", "merge.option.keywordIndent", anchor);
                    Boundary(open.Offset, "merge.option.breakAfter", "merge.option.hintsIndent", keyword.Offset);
                    foreach (var hint in statement.OptimizerHints)
                        editor.Before(hint.StartOffset, "inherit", Indent("merge.option.hintsIndent"), anchorOffset: keyword.Offset);
                }
            }
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var after = parser.Parse(changed, dialect, cancellationToken);
        return after.ParseSucceeded && SqlSpacing.SameTokens(parsed, after) ? changed : source;

        void Output(TSqlFragment clause, TSqlFragment[] items, int anchor)
        {
            if (items.Length == 0) return;
            var keyword = editor.Find(clause.StartOffset, items[0].StartOffset, Is("OUTPUT"));
            if (keyword is null) return;
            Boundary(keyword.Offset, "merge.output.breakBefore", "merge.output.keywordIndent", anchor);
            Boundary(items[0].StartOffset, "merge.output.breakAfter", "merge.output.listIndent", keyword.Offset);
            lists.Apply(items, "merge.output", keyword.Offset, keyword.Offset);
        }
        void Boundary(int offset, string key, string indent, int anchor) => editor.Before(offset, Choice(key), Indent(indent), anchorOffset: anchor);
        string Choice(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, key).Indent;
    }
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
