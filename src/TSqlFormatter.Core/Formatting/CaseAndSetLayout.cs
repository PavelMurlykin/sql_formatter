using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Applies opt-in CASE and set-operator layout to parsed token boundaries.</summary>
internal static class CaseAndSetLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("case.", StringComparison.Ordinal)
            || key.StartsWith("setOperator.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        foreach (var fragment in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fragment is CaseExpression expression) ProcessCase(expression);
            else if (fragment is BinaryQueryExpression binary) ProcessSetOperator(binary);
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SqlSpacing.SameTokens(parsed, validated) ? changed : source;

        void ProcessCase(CaseExpression expression)
        {
            var end = expression.StartOffset + expression.FragmentLength;
            var caseToken = editor.Find(expression.StartOffset, end, Is("CASE"));
            var endToken = editor.FindLast(expression.StartOffset, end, Is("END"));
            if (caseToken is null || endToken is null) return;
            editor.Before(caseToken.Offset, Break("case.breakBeforeCase"), Indent("case.caseIndent"));
            editor.Before(endToken.Offset, Break("case.breakBeforeEnd"), anchorOffset: caseToken.Offset);
            if (expression is SimpleCaseExpression { InputExpression: { } input })
                editor.Before(input.StartOffset, Break("case.breakBeforeInput"),
                    Indent("case.inputIndent"));

            IList<WhenClause> clauses = expression switch
            {
                SimpleCaseExpression simple => simple.WhenClauses.Cast<WhenClause>().ToList(),
                SearchedCaseExpression searched => searched.WhenClauses.Cast<WhenClause>().ToList(),
                _ => Array.Empty<WhenClause>()
            };
            foreach (var clause in clauses)
            {
                var condition = clause switch
                {
                    SimpleWhenClause simple => (TSqlFragment?)simple.WhenExpression,
                    SearchedWhenClause searched => searched.WhenExpression,
                    _ => null
                };
                if (condition is null || clause.ThenExpression is null) continue;
                var when = editor.Find(clause.StartOffset, condition.StartOffset, Is("WHEN"));
                var then = editor.Find(condition.StartOffset + condition.FragmentLength,
                    clause.ThenExpression.StartOffset, Is("THEN"));
                if (when is null || then is null) continue;
                editor.Before(when.Offset, Break("case.breakBeforeWhenElse"),
                    Indent("case.whenKeywordIndent"), anchorOffset: caseToken.Offset);
                editor.Before(condition.StartOffset, "inherit", Indent("case.whenExpressionIndent"));
                editor.Before(then.Offset, Break("case.breakBeforeThen"),
                    Indent("case.thenKeywordIndent"));
                editor.Before(clause.ThenExpression.StartOffset, Break("case.breakAfterThenElse"),
                    Indent("case.codeIndent"));
                if (clause is SearchedWhenClause { WhenExpression: { } boolean })
                    ProcessCondition(boolean);
            }
            if (expression.ElseExpression is not { } otherwise) return;
            var afterLast = clauses.Count == 0 ? caseToken.Offset + caseToken.Text.Length
                : clauses[clauses.Count - 1].StartOffset + clauses[clauses.Count - 1].FragmentLength;
            var elseToken = editor.Find(afterLast, otherwise.StartOffset, Is("ELSE"));
            if (elseToken is null) return;
            editor.Before(elseToken.Offset, Break("case.breakBeforeWhenElse"),
                Indent("case.whenKeywordIndent"), anchorOffset: caseToken.Offset);
            editor.Before(otherwise.StartOffset, Break("case.breakAfterThenElse"),
                Indent("case.codeIndent"));
        }

        void ProcessCondition(BooleanExpression expression)
        {
            if (expression is BooleanParenthesisExpression { Expression: { } inner })
            {
                editor.Before(inner.StartOffset, "inherit", Indent("case.nestedConditionIndent"));
                ProcessCondition(inner);
            }
            else if (expression is BooleanBinaryExpression { FirstExpression: { } first,
                         SecondExpression: { } second })
            {
                ProcessCondition(first);
                ProcessCondition(second);
                var op = editor.Find(first.StartOffset + first.FragmentLength, second.StartOffset,
                    token => Is("AND")(token) || Is("OR")(token));
                if (op is null) return;
                var mode = Break("case.wrapCondition");
                var match = mode == "both" || mode == "and" && Is("AND")(op)
                    || mode == "or" && Is("OR")(op);
                var before = Break("case.wrapBeforeOperator");
                var after = Break("case.wrapAfterOperator");
                if (before == "inherit" && mode != "inherit") before = match ? "always" : "never";
                if (after == "inherit" && mode != "inherit") after = "never";
                editor.Before(op.Offset, before);
                editor.Before(second.StartOffset, after, Indent("case.nestedConditionIndent"));
            }
        }

        void ProcessSetOperator(BinaryQueryExpression binary)
        {
            if (binary.FirstQueryExpression is not { } left
                || binary.SecondQueryExpression is not { } right) return;
            var word = binary.BinaryQueryExpressionType switch
            {
                BinaryQueryExpressionType.Union => "UNION",
                BinaryQueryExpressionType.Except => "EXCEPT",
                BinaryQueryExpressionType.Intersect => "INTERSECT",
                _ => null
            };
            if (word is null) return;
            var op = editor.Find(left.StartOffset + left.FragmentLength, right.StartOffset, Is(word));
            if (op is null) return;
            editor.Before(op.Offset, Break("setOperator.breakBefore"),
                Indent("setOperator.keywordIndent"), anchorOffset:
                    SelectQueryScope.UsesSpaceOffsets(options) ? left.StartOffset : null);
            var last = binary.All ? editor.Find(op.Offset + op.Text.Length,
                right.StartOffset, Is("ALL")) : null;
            editor.After((last ?? op).Offset, Break("setOperator.breakAfter"),
                Indent("setOperator.branchIndent"), anchorOffset:
                    SelectQueryScope.UsesSpaceOffsets(options) ? op.Offset : null);
        }

        string Break(string key) => NativeRules.Get(options, key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, key).Indent;
    }

    private static Func<TSqlParserToken, bool> Is(string word) => token =>
        token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
