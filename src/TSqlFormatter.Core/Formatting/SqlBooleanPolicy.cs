using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Shared AND/OR layout for ON, WHERE, and HAVING without shared rule values.</summary>
internal static class SqlBooleanPolicy
{
    public static void Apply(BooleanExpression expression, SqlTokenGapEditor editor,
        FormattingOptions options, string prefix, int? anchorOffset = null)
    {
        if (expression is BooleanParenthesisExpression { Expression: { } inner })
        {
            var open = editor.Find(expression.StartOffset, inner.StartOffset, token => token.Text == "(");
            if (open is not null)
                editor.Before(inner.StartOffset, "inherit", Indent("nestedConditionIndent"), anchorOffset: anchorOffset);
            Apply(inner, editor, options, prefix, anchorOffset);
        }
        else if (expression is BooleanNotExpression { Expression: { } negated })
            Apply(negated, editor, options, prefix, anchorOffset);
        else if (expression is BooleanBinaryExpression { FirstExpression: { } first,
                     SecondExpression: { } second })
        {
            Apply(first, editor, options, prefix, anchorOffset);
            Apply(second, editor, options, prefix, anchorOffset);
            var op = editor.Find(first.StartOffset + first.FragmentLength, second.StartOffset,
                token => token.Text.Equals("AND", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("OR", StringComparison.OrdinalIgnoreCase));
            if (op is null) return;
            var mode = NativeRules.Get(options, prefix + ".wrapCondition").Choice;
            var match = mode == "both" || mode == "and" && op.Text.Equals("AND", StringComparison.OrdinalIgnoreCase)
                || mode == "or" && op.Text.Equals("OR", StringComparison.OrdinalIgnoreCase);
            var before = NativeRules.Get(options, prefix + ".wrapBeforeOperator").Choice;
            var after = NativeRules.Get(options, prefix + ".wrapAfterOperator").Choice;
            if (before == "inherit" && mode != "inherit") before = match ? "always" : "never";
            if (after == "inherit" && mode != "inherit") after = "never";
            editor.Before(op.Offset, before, anchorOffset: anchorOffset);
            editor.Before(second.StartOffset, after, Indent("nestedConditionIndent"), anchorOffset: anchorOffset);
        }

        IndentRule Indent(string suffix) => NativeRules.Get(options, prefix + "." + suffix).Indent;
    }
}
