using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Analysis.Rules;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Analysis;

/// <summary>
/// Facade that runs the configured rules over a parsed T-SQL script.
/// When parsing failed and no AST is available, the result is empty: parse
/// diagnostics come from <see cref="SqlParseResult.Diagnostics"/>.
/// </summary>
public sealed class SqlAnalyzer
{
    public static IReadOnlyList<IAnalysisRule> DefaultRules { get; } = new IAnalysisRule[]
    {
        new SelectStarRule(),
        new InsertWithoutColumnListRule(),
        new TopWithoutOrderByRule(),
        new UpdateWithoutWhereRule(),
        new DeleteWithoutWhereRule()
    };

    private readonly IReadOnlyList<IAnalysisRule> _rules;

    public SqlAnalyzer()
        : this(DefaultRules)
    {
    }

    public SqlAnalyzer(IEnumerable<IAnalysisRule> rules)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        _rules = rules.ToArray();
        foreach (var rule in _rules)
        {
            if (rule is null)
            {
                throw new ArgumentException("The rule collection must not contain null entries.", nameof(rules));
            }
        }
    }

    public AnalysisResult Analyze(
        SqlParseResult parseResult,
        AnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        cancellationToken.ThrowIfCancellationRequested();
        options ??= AnalysisOptions.Default;

        var diagnostics = new List<AnalysisDiagnostic>();
        if (parseResult.Root is TSqlFragment root)
        {
            foreach (var rule in _rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!options.IsRuleEnabled(rule.Code))
                {
                    continue;
                }

                foreach (var finding in rule.Analyze(root, cancellationToken))
                {
                    var position = parseResult.LineMap.GetLinePosition(finding.Span.StartOffset);
                    diagnostics.Add(new AnalysisDiagnostic(
                        rule.Code,
                        options.GetSeverity(rule.Code, rule.DefaultSeverity),
                        finding.Message,
                        finding.Span,
                        position.Line,
                        position.Column));
                }
            }
        }

        diagnostics.Sort(CompareDiagnostics);
        return new AnalysisResult(Array.AsReadOnly(diagnostics.ToArray()));
    }

    private static int CompareDiagnostics(AnalysisDiagnostic left, AnalysisDiagnostic right)
    {
        var byOffset = left.Span.StartOffset.CompareTo(right.Span.StartOffset);
        return byOffset != 0 ? byOffset : string.CompareOrdinal(left.Code, right.Code);
    }
}
