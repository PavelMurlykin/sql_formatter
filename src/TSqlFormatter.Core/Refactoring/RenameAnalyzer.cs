using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Refactoring;

/// <summary>
/// Finds a local variable or table alias under an offset and all its occurrences:
/// variables are scoped to their batch, aliases to the query specification that
/// declares them (including correlated references); same-name aliases declared in
/// nested queries shadow the outer symbol and are not renamed.
/// </summary>
public sealed class RenameAnalyzer
{
    public RenameSymbol? FindSymbol(SqlParseResult parseResult, int offset)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        if (offset < 0 || offset > parseResult.Source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (parseResult.Root is null)
        {
            return null;
        }

        var nodes = new SqlFragmentWalker().Walk(parseResult.Root);
        var token = FindTokenAt(parseResult, offset);
        if (token is null)
        {
            return null;
        }

        if (token.TokenType == TSqlTokenType.Variable)
        {
            return FindVariable(nodes, token);
        }

        return FindAlias(nodes, offset, token.Text);
    }

    /// <summary>Maps every occurrence of the symbol to a replacement; validates the new name by kind.</summary>
    public IReadOnlyList<TextEdit> Plan(RenameSymbol symbol, string newName)
    {
        if (symbol is null)
        {
            throw new ArgumentNullException(nameof(symbol));
        }

        if (newName is null)
        {
            throw new ArgumentNullException(nameof(newName));
        }

        var valid = symbol.Kind == RenameSymbolKind.LocalVariable
            ? RenameNames.IsValidVariableName(newName)
            : RenameNames.IsValidIdentifier(newName);
        if (!valid)
        {
            throw new ArgumentException(
                symbol.Kind == RenameSymbolKind.LocalVariable
                    ? "The new variable name must be @ followed by an identifier."
                    : "The new alias must be a plain identifier of at most 128 characters, not a reserved SQL word.",
                nameof(newName));
        }

        return symbol.Occurrences.Select(span => new TextEdit(span, newName)).ToArray();
    }

    private static RenameSymbol? FindVariable(IReadOnlyList<TSqlFragment> nodes, TSqlParserToken token)
    {
        var batch = nodes.OfType<TSqlBatch>()
            .Where(batch => Contains(batch, token.Offset, token.Text.Length))
            .OrderBy(batch => batch.FragmentLength)
            .FirstOrDefault();
        if (batch is null)
        {
            return null;
        }

        var spans = new List<SqlTextSpan>();
        foreach (var declaration in nodes.OfType<DeclareVariableElement>())
        {
            var variableName = declaration.VariableName;
            if (variableName is not null &&
                Contains(batch, declaration.StartOffset, declaration.FragmentLength) &&
                string.Equals(variableName.Value, token.Text, StringComparison.OrdinalIgnoreCase))
            {
                spans.Add(new SqlTextSpan(variableName.StartOffset, variableName.FragmentLength));
            }
        }

        foreach (var reference in nodes.OfType<VariableReference>())
        {
            if (Contains(batch, reference.StartOffset, reference.FragmentLength) &&
                string.Equals(reference.Name, token.Text, StringComparison.OrdinalIgnoreCase))
            {
                spans.Add(new SqlTextSpan(reference.StartOffset, reference.FragmentLength));
            }
        }

        return DistinctSorted(spans) is { Count: > 0 } occurrences
            ? new RenameSymbol(RenameSymbolKind.LocalVariable, token.Text, occurrences)
            : null;
    }

    private static RenameSymbol? FindAlias(IReadOnlyList<TSqlFragment> nodes, int offset, string tokenText)
    {
        var scope = AliasScope.Build(nodes);
        if (scope is null || !scope.TryResolveAtOffset(offset, tokenText, out var declaring))
        {
            return null;
        }

        var spans = new List<SqlTextSpan>();
        foreach (var declaration in scope.GetDeclarationOccurrences(declaring))
        {
            spans.Add(declaration);
        }

        foreach (var column in nodes.OfType<ColumnReferenceExpression>())
        {
            var identifiers = column.MultiPartIdentifier?.Identifiers;
            if (identifiers is null || identifiers.Count < 2)
            {
                continue;
            }

            for (var index = 0; index < identifiers.Count - 1; index++)
            {
                AddQualifierIfBound(scope, declaring, identifiers[index], spans);
            }
        }

        foreach (var star in nodes.OfType<SelectStarExpression>())
        {
            var identifiers = star.Qualifier?.Identifiers;
            if (identifiers is null)
            {
                continue;
            }

            foreach (var identifier in identifiers)
            {
                AddQualifierIfBound(scope, declaring, identifier, spans);
            }
        }

        return DistinctSorted(spans) is { Count: > 0 } occurrences
            ? new RenameSymbol(RenameSymbolKind.TableAlias, declaring.Identifier.Value, occurrences)
            : null;
    }

    private static void AddQualifierIfBound(
        AliasScope scope, AliasDeclaration declaring, Identifier identifier, List<SqlTextSpan> spans)
    {
        if (identifier.QuoteType != QuoteType.NotQuoted ||
            !string.Equals(identifier.Value, declaring.Identifier.Value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var span = new SqlTextSpan(identifier.StartOffset, identifier.FragmentLength);
        if (scope.IsBoundTo(declaring, span))
        {
            spans.Add(span);
        }
    }

    private static TSqlParserToken? FindTokenAt(SqlParseResult parseResult, int offset)
    {
        foreach (var token in parseResult.Tokens)
        {
            if (token.Offset <= offset && offset < token.Offset + token.Text.Length)
            {
                return token;
            }
        }

        return null;
    }

    private static bool Contains(TSqlFragment fragment, int offset, int length)
    {
        var end = fragment.StartOffset + fragment.FragmentLength;
        return fragment.StartOffset <= offset && offset + length <= end;
    }

    private static bool ContainsSpan(SqlTextSpan outer, SqlTextSpan inner) =>
        outer.StartOffset <= inner.StartOffset && inner.EndOffset <= outer.EndOffset;

    private static IReadOnlyList<SqlTextSpan>? DistinctSorted(List<SqlTextSpan> spans)
    {
        var ordered = spans
            .GroupBy(span => span.StartOffset)
            .Select(group => group.First())
            .OrderBy(span => span.StartOffset)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].EndOffset > ordered[index].StartOffset)
            {
                return null;
            }
        }

        return ordered;
    }

    private sealed class AliasDeclaration
    {
        public AliasDeclaration(Identifier identifier, SqlTextSpan span)
        {
            Identifier = identifier;
            Span = span;
        }

        public Identifier Identifier { get; }

        public SqlTextSpan Span { get; }
    }

    /// <summary>
    /// Resolves alias names through the chain of nested query specifications:
    /// a reference binds to the closest same-name alias declared in its query
    /// or in an enclosing query, so nested same-name aliases shadow the outer one.
    /// </summary>
    private sealed class AliasScope
    {
        private readonly IReadOnlyList<QuerySpecification> queries;
        private readonly Dictionary<QuerySpecification, List<AliasDeclaration>> declarationsByQuery;

        private AliasScope(
            IReadOnlyList<AliasDeclaration> declarations,
            IReadOnlyList<QuerySpecification> queries,
            Dictionary<QuerySpecification, List<AliasDeclaration>> declarationsByQuery)
        {
            Declarations = declarations;
            this.queries = queries;
            this.declarationsByQuery = declarationsByQuery;
        }

        public IReadOnlyList<AliasDeclaration> Declarations { get; }

        public static AliasScope? Build(IReadOnlyList<TSqlFragment> nodes)
        {
            var declarations = nodes.OfType<NamedTableReference>()
                .Where(reference => reference.Alias is { QuoteType: QuoteType.NotQuoted })
                .Select(reference => new AliasDeclaration(
                    reference.Alias!,
                    new SqlTextSpan(reference.Alias!.StartOffset, reference.Alias.FragmentLength)))
                .ToList();
            if (declarations.Count == 0)
            {
                return null;
            }

            var queries = nodes.OfType<QuerySpecification>().ToList();
            var byQuery = new Dictionary<QuerySpecification, List<AliasDeclaration>>();
            foreach (var declaration in declarations)
            {
                var query = SmallestContaining(queries, declaration.Span);
                if (query is null)
                {
                    continue;
                }

                if (!byQuery.TryGetValue(query, out var list))
                {
                    byQuery[query] = list = new List<AliasDeclaration>();
                }

                list.Add(declaration);
            }

            return new AliasScope(declarations, queries, byQuery);
        }

        public bool TryResolveAtOffset(int offset, string tokenText, out AliasDeclaration declaring)
        {
            declaring = Declarations
                .Where(declaration => declaration.Span.StartOffset <= offset && offset < declaration.Span.EndOffset)
                .OrderBy(declaration => declaration.Span.Length)
                .FirstOrDefault()!;
            if (declaring is not null)
            {
                return true;
            }

            var query = SmallestContainingQueryAt(queries, offset);
            while (query is not null)
            {
                if (declarationsByQuery.TryGetValue(query, out var level))
                {
                    foreach (var declaration in level)
                    {
                        if (SameName(declaration, tokenText))
                        {
                            declaring = declaration;
                            return true;
                        }
                    }
                }

                query = ParentQuery(query);
            }

            return false;
        }

        /// <summary>Alias declaration spans with the same name that bind to the declaring alias.</summary>
        public IEnumerable<SqlTextSpan> GetDeclarationOccurrences(AliasDeclaration declaring)
        {
            foreach (var declaration in Declarations)
            {
                if (SameName(declaration, declaring.Identifier.Value) &&
                    IsBoundTo(declaring, declaration.Span))
                {
                    yield return declaration.Span;
                }
            }
        }

        public bool IsBoundTo(AliasDeclaration declaring, SqlTextSpan span)
        {
            var declaringQuery = SmallestContaining(queries, declaring.Span);
            var query = SmallestContaining(queries, span);
            while (query is not null)
            {
                if (ReferenceEquals(query, declaringQuery))
                {
                    return true;
                }

                if (declarationsByQuery.TryGetValue(query, out var level) &&
                    level.Any(declaration => SameName(declaration, declaring.Identifier.Value)))
                {
                    // A same-name alias at this level shadows the outer declaration.
                    return false;
                }

                query = ParentQuery(query);
            }

            return false;
        }

        private static bool SameName(AliasDeclaration declaration, string name) =>
            string.Equals(declaration.Identifier.Value, name, StringComparison.OrdinalIgnoreCase);

        private QuerySpecification? SmallestContainingQueryAt(IReadOnlyList<QuerySpecification> candidates, int offset) =>
            candidates
                .Where(query => query.StartOffset <= offset && offset < query.StartOffset + query.FragmentLength)
                .OrderBy(query => query.FragmentLength)
                .FirstOrDefault();

        private static QuerySpecification? SmallestContaining(IReadOnlyList<QuerySpecification> candidates, SqlTextSpan span) =>
            candidates
                .Where(query => ContainsSpan(
                    new SqlTextSpan(query.StartOffset, query.FragmentLength), span))
                .OrderBy(query => query.FragmentLength)
                .FirstOrDefault();

        private QuerySpecification? ParentQuery(QuerySpecification query) =>
            SmallestContaining(
                queries.Where(candidate => !ReferenceEquals(candidate, query)).ToList(),
                new SqlTextSpan(query.StartOffset, query.FragmentLength));
    }
}
