using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Opt-in whitespace rules confined to syntactically identified token gaps.</summary>
internal static class SqlSpacing
{
    private static readonly string[] Keys =
    {
        "spacing.arithmeticOperators", "spacing.beforeComma", "spacing.afterComma",
        "spacing.beforeDot", "spacing.afterDot", "spacing.beforeScopeResolution",
        "spacing.afterScopeResolution", "spacing.beforeFunctionArguments",
        "spacing.withinEmptyFunctionArguments", "spacing.withinFunctionArguments"
    };

    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (Keys.All(key => Get(options, key) == "inherit")) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded) return source;
        var edits = GetEdits(parsed, options, cancellationToken);
        if (edits.Count == 0) return source;
        var changed = KeywordCasing.Apply(source, edits, cancellationToken);
        var validated = parser.Parse(changed, dialect, cancellationToken);
        return validated.ParseSucceeded && SameTokens(parsed, validated) ? changed : source;
    }

    private static IReadOnlyList<TextEdit> GetEdits(SqlParseResult parsed, FormattingOptions options,
        CancellationToken cancellationToken)
    {
        var arithmetic = new HashSet<int>();
        var functionOpens = new HashSet<int>();
        var functionCloses = new HashSet<int>();
        var emptyFunctionOpens = new HashSet<int>();
        var tokens = parsed.Tokens.Where(Meaningful).ToArray();
        if (parsed.Root is not null)
        {
            foreach (var fragment in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fragment is BinaryExpression binary &&
                    Get(options, "spacing.arithmeticOperators") != "inherit")
                {
                    var firstEnd = binary.FirstExpression.StartOffset + binary.FirstExpression.FragmentLength;
                    var secondStart = binary.SecondExpression.StartOffset;
                    var op = tokens.FirstOrDefault(token => token.Offset >= firstEnd && token.Offset < secondStart
                        && IsArithmetic(token.Text));
                    if (op is not null) arithmetic.Add(op.Offset);
                }
                if (fragment is FunctionCall call && call.FunctionName is not null)
                {
                    var nameEnd = call.FunctionName.StartOffset + call.FunctionName.FragmentLength;
                    var callEnd = call.StartOffset + call.FragmentLength;
                    var openIndex = Array.FindIndex(tokens, token => token.Offset >= nameEnd
                        && token.Offset < callEnd && token.Text == "(");
                    if (openIndex < 0) continue;
                    var depth = 0;
                    for (var index = openIndex; index < tokens.Length && tokens[index].Offset < callEnd; index++)
                    {
                        if (tokens[index].Text == "(") depth++;
                        if (tokens[index].Text == ")" && --depth == 0)
                        {
                            functionOpens.Add(tokens[openIndex].Offset);
                            functionCloses.Add(tokens[index].Offset);
                            if (index == openIndex + 1) emptyFunctionOpens.Add(tokens[openIndex].Offset);
                            break;
                        }
                    }
                }
            }
        }

        var edits = new List<TextEdit>();
        for (var index = 1; index < tokens.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = tokens[index - 1];
            var right = tokens[index];
            if (Comment(left) || Comment(right)) continue;
            var start = left.Offset + left.Text.Length;
            var length = right.Offset - start;
            if (length < 0) continue;
            var gap = parsed.Source.Substring(start, length);
            if (gap.Any(ch => ch is '\r' or '\n') || gap.Any(ch => !char.IsWhiteSpace(ch))) continue;
            var rule = SelectRule(left, right, arithmetic, functionOpens, functionCloses,
                emptyFunctionOpens);
            if (rule is null) continue;
            var setting = Get(options, rule);
            if (setting == "inherit") continue;
            var replacement = setting == "insert" ? " " : string.Empty;
            if (gap != replacement) edits.Add(new TextEdit(new SqlTextSpan(start, length), replacement));
        }
        return edits;
    }

    private static string? SelectRule(TSqlParserToken left, TSqlParserToken right,
        HashSet<int> arithmetic, HashSet<int> functionOpens,
        HashSet<int> functionCloses, HashSet<int> emptyFunctionOpens)
    {
        if (right.Text == ",") return "spacing.beforeComma";
        if (left.Text == ",") return "spacing.afterComma";
        if (right.Text == ".") return "spacing.beforeDot";
        if (left.Text == ".") return "spacing.afterDot";
        if (right.Text == "::") return "spacing.beforeScopeResolution";
        if (left.Text == "::") return "spacing.afterScopeResolution";
        if (right.Text == "(" && functionOpens.Contains(right.Offset))
            return "spacing.beforeFunctionArguments";
        if (left.Text == "(" && emptyFunctionOpens.Contains(left.Offset) && right.Text == ")")
            return "spacing.withinEmptyFunctionArguments";
        if ((left.Text == "(" && functionOpens.Contains(left.Offset))
            || (right.Text == ")" && functionCloses.Contains(right.Offset)))
            return "spacing.withinFunctionArguments";
        if (arithmetic.Contains(left.Offset) || arithmetic.Contains(right.Offset))
            return "spacing.arithmeticOperators";
        return null;
    }

    private static bool IsArithmetic(string text) => text is "+" or "-" or "*" or "/" or "%"
        or "&" or "|" or "^";

    private static bool Comment(TSqlParserToken token) => token.TokenType is
        TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment;

    private static bool Meaningful(TSqlParserToken token) => token.TokenType is not
        (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile) && token.Text.Length > 0;

    private static string Get(FormattingOptions options, string key) =>
        options.Rules.Catalog.TryGet(key, out _) ? options.Rules.Get(key).Choice : "inherit";

    internal static bool SameTokens(SqlParseResult before, SqlParseResult after)
    {
        var a = before.Tokens.Where(Meaningful).ToArray();
        var b = after.Tokens.Where(Meaningful).ToArray();
        return a.Length == b.Length && a.Zip(b, (left, right) =>
            left.TokenType == right.TokenType && left.Text == right.Text).All(equal => equal);
    }
}
