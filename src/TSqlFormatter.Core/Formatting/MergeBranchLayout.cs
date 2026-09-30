using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>MERGE WHEN/THEN and action policies, independent of source/header rules.</summary>
internal static class MergeBranchLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("merge.when.", StringComparison.Ordinal)
            || key.StartsWith("merge.then.", StringComparison.Ordinal) || key.StartsWith("merge.update.", StringComparison.Ordinal)
            || key.StartsWith("merge.insert.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var lists = new SqlListLayout(parsed, options, editor);
        foreach (var spec in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken).OfType<MergeSpecification>())
        {
            var branchStart = End(spec.SearchCondition);
            foreach (var clause in spec.ActionClauses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (clause.Action is not { } action) continue;
                var when = editor.Find(branchStart, action.StartOffset, Is("WHEN"));
                var then = editor.FindLast(branchStart, action.StartOffset, Is("THEN"));
                branchStart = End(action);
                if (when is null || then is null) continue;
                Boundary(when.Offset, "merge.when.breakBefore", "merge.when.keywordIndent", spec.StartOffset);
                editor.After(when.Offset, Choice("merge.when.breakAfter"), Indent("merge.when.conditionIndent"), anchorOffset: when.Offset);
                if (clause.SearchCondition is { } condition)
                    SqlBooleanPolicy.Apply(condition, editor, options, "merge.when", when.Offset);
                Boundary(then.Offset, "merge.then.breakBefore", "merge.then.keywordIndent", when.Offset);
                Boundary(action.StartOffset, "merge.then.breakAfter", "merge.then.actionIndent", then.Offset);
                if (action is UpdateMergeAction { SetClauses.Count: > 0 } update)
                {
                    var prefix = NativeRules.Get(options, "merge.update.useStatementFormatting").Boolean ? "update.set" : "merge.update.set";
                    var items = update.SetClauses.Cast<TSqlFragment>().ToArray();
                    var set = editor.Find(action.StartOffset, items[0].StartOffset, Is("SET"));
                    if (set is null) continue;
                    Boundary(set.Offset, prefix + ".breakBefore", prefix + ".keywordIndent", action.StartOffset);
                    Boundary(items[0].StartOffset, prefix + ".breakAfter", prefix + ".listIndent", set.Offset);
                    if (lists.Apply(items, prefix, set.Offset, set.Offset))
                        foreach (var assignment in update.SetClauses.OfType<AssignmentSetClause>())
                            if (assignment.Column is { } column && assignment.NewValue is { } value)
                            {
                                var equals = editor.Find(End(column), value.StartOffset, t => t.Text == "=");
                                if (equals is not null) editor.Before(equals.Offset, "inherit", spaceMode: "insert");
                            }
                }
                else if (action is InsertMergeAction insert)
                {
                    var prefix = NativeRules.Get(options, "merge.insert.useStatementFormatting").Boolean ? "insert" : "merge.insert";
                    if (insert.Columns.Count > 0)
                    {
                        var items = insert.Columns.Cast<TSqlFragment>().ToArray();
                        var open = editor.Find(action.StartOffset, items[0].StartOffset, t => t.Text == "(");
                        var close = editor.Find(End(items[items.Length - 1]), insert.Source.StartOffset, t => t.Text == ")");
                        if (open is not null && close is not null)
                        {
                            editor.Before(open.Offset, Choice(prefix + ".columns.breakBeforeOpen"), Indent(prefix + ".columns.braceIndent"),
                                Choice(prefix + ".columns.spaceBeforeOpen"), action.StartOffset);
                            Braced(items, open, close, prefix + ".columns", action.StartOffset);
                        }
                    }
                    if (insert.Source is ValuesInsertSource { RowValues.Count: > 0 } values)
                    {
                        var keyword = editor.Find(values.StartOffset, values.RowValues[0].StartOffset, Is("VALUES"));
                        if (keyword is null) continue;
                        Boundary(keyword.Offset, prefix + ".values.breakBeforeKeyword", prefix + ".values.keywordIndent", action.StartOffset);
                        editor.Before(values.RowValues[0].StartOffset, Choice(prefix + ".values.breakAfterKeyword"),
                            Indent(prefix + ".values.braceIndent"), Choice(prefix + ".values.spaceAfterKeyword"), keyword.Offset);
                        foreach (var row in values.RowValues)
                        {
                            if (row.ColumnValues.Count == 0) continue;
                            var items = row.ColumnValues.Cast<TSqlFragment>().ToArray();
                            var open = editor.Find(row.StartOffset, items[0].StartOffset, t => t.Text == "(");
                            var close = editor.Find(End(items[items.Length - 1]), End(row), t => t.Text == ")");
                            if (open is not null && close is not null) Braced(items, open, close, prefix + ".values", keyword.Offset);
                        }
                    }
                }
            }
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var after = parser.Parse(changed, dialect, cancellationToken);
        return after.ParseSucceeded && SqlSpacing.SameTokens(parsed, after) ? changed : source;

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
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
