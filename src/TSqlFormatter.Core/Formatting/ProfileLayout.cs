using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

/// <summary>Optional shared layout policies for imported native profiles, including nested statements.</summary>
internal static class ProfileLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken, string? originalSource = null)
    {
        if (!options.Rules.Overrides.Keys.Any(k => k.StartsWith("layout.", StringComparison.Ordinal))) return source;
        source = Pass(source, Layout);
        source = SqlSpacing.ApplySafe(source, options, parser, dialect, cancellationToken);
        source = Pass(source, Compact);
        source = SqlSpacing.ApplySafe(source, options, parser, dialect, cancellationToken);
        source = Pass(source, p => Align(p, true));
        source = Pass(source, p => Align(p, false));
        source = Pass(source, Separate);
        source = Pass(source, Whitespace);
        return Flag("respectFormattingDirectives") && originalSource is not null
            ? RestoreDirectives(originalSource, source, parser, dialect, cancellationToken) : source;

        string Pass(string text, Func<SqlParseResult, string> apply)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = parser.Parse(text, dialect, cancellationToken);
            if (!before.ParseSucceeded || before.Root is null) return text;
            var changed = apply(before);
            if (changed == text) return text;
            var after = parser.Parse(changed, dialect, cancellationToken);
            return after.ParseSucceeded && SqlSpacing.SameTokens(before, after) ? changed : text;
        }

        string Layout(SqlParseResult parsed)
        {
            var editor = new SqlTokenGapEditor(parsed, options);
            var indent = NativeRules.Get(options, "layout.listIndent").Indent;
            var firstMode = Choice("listFirstItem");
            var fragments = Fragments(parsed).ToArray();
            foreach (var f in fragments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (f)
                {
                    case BeginEndBlockStatement block when firstMode != "inherit":
                        var blockAnchor = editor.GetLineIndent(block.StartOffset);
                        foreach (var statement in block.StatementList.Statements)
                            editor.BeforeAtColumn(statement.StartOffset, blockAnchor + options.Indent.Size);
                        var endBlock = editor.FindLast(block.StartOffset, End(block), Is("END"));
                        if (endBlock is not null) editor.BeforeAtColumn(endBlock.Offset, blockAnchor);
                        break;
                    case TryCatchStatement handler when firstMode != "inherit":
                        var handlerColumn = editor.GetLineIndent(handler.StartOffset);
                        foreach (var list in new[] { handler.TryStatements, handler.CatchStatements })
                            foreach (var statement in list.Statements) editor.BeforeAtColumn(statement.StartOffset, handlerColumn + options.Indent.Size);
                        foreach (var word in new[] { "END", "BEGIN" })
                        {
                            var last = handler.TryStatements.Statements.LastOrDefault();
                            if (last is null) continue;
                            var boundary = editor.Find(End(last), handler.CatchStatements.StartOffset, Is(word));
                            if (boundary is not null) editor.BeforeAtColumn(boundary.Offset, handlerColumn);
                        }
                        var catchEnd = editor.FindLast(handler.StartOffset, End(handler), Is("END"));
                        if (catchEnd is not null) editor.BeforeAtColumn(catchEnd.Offset, handlerColumn);
                        break;
                    case IfStatement conditional when firstMode != "inherit":
                        var ifColumn = editor.GetLineIndent(conditional.StartOffset);
                        if (conditional.ThenStatement is { } then)
                            editor.BeforeAtColumn(then.StartOffset, ifColumn + (then is BeginEndBlockStatement ? 0 : options.Indent.Size));
                        if (conditional.ElseStatement is { } alternative)
                        {
                            var elseToken = editor.FindLast(conditional.StartOffset, alternative.StartOffset, Is("ELSE"));
                            if (elseToken is not null) editor.BeforeAtColumn(elseToken.Offset, ifColumn);
                            editor.BeforeAtColumn(alternative.StartOffset, ifColumn + (alternative is BeginEndBlockStatement ? 0 : options.Indent.Size));
                        }
                        break;
                    case WhileStatement loop when firstMode != "inherit":
                        editor.BeforeAtColumn(loop.Statement.StartOffset, editor.GetLineIndent(loop.StartOffset)
                            + (loop.Statement is BeginEndBlockStatement ? 0 : options.Indent.Size));
                        break;
                    case QuerySpecification q when firstMode != "inherit":
                        var cteOwner = fragments.OfType<SelectStatement>().FirstOrDefault(s => s.QueryExpression == q && s.WithCtesAndXmlNamespaces is not null);
                        if (cteOwner is not null) editor.BeforeAtColumn(q.StartOffset, editor.GetLineIndent(cteOwner.StartOffset));
                        List(q.SelectElements.Cast<TSqlFragment>().ToArray(), q.StartOffset, firstMode,
                            q.TopRowFilter is not null || q.UniqueRowFilter != UniqueRowFilter.NotSpecified ? Flag("newLineAfterTop") : false);
                        Clause(q.FromClause, q.FromClause?.TableReferences.FirstOrDefault(), q.StartOffset, "never");
                        Clause(q.WhereClause, q.WhereClause?.SearchCondition, q.StartOffset, "never");
                        Clause(q.HavingClause, q.HavingClause?.SearchCondition, q.StartOffset, "never");
                        if (q.GroupByClause is { } group)
                        {
                            editor.Before(group.StartOffset, "always", Zero(), anchorOffset: q.StartOffset);
                            List(group.GroupingSpecifications.Cast<TSqlFragment>().ToArray(), q.StartOffset, firstMode);
                        }
                        if (q.OrderByClause is { } order)
                        {
                            editor.Before(order.StartOffset, "always", Zero(), anchorOffset: q.StartOffset);
                            List(order.OrderByElements.Cast<TSqlFragment>().ToArray(), q.StartOffset, firstMode);
                        }
                        if (q.FromClause is { } from)
                            foreach (var join in FragmentsIn(from).OfType<JoinTableReference>()) Join(join, q.StartOffset);
                        if (q.WhereClause?.SearchCondition is { } where) Boolean(where, q.StartOffset);
                        if (q.HavingClause?.SearchCondition is { } having) Boolean(having, q.StartOffset);
                        break;
                    case DeclareVariableStatement d when firstMode != "inherit":
                        List(d.Declarations.Cast<TSqlFragment>().ToArray(), d.StartOffset, firstMode);
                        break;
                    case ExecuteStatement execute when firstMode != "inherit" && execute.ExecuteSpecification?.ExecutableEntity is { Parameters.Count: > 0 } entity:
                        List(entity.Parameters.Cast<TSqlFragment>().ToArray(), execute.StartOffset, "always");
                        break;
                    case UpdateSpecification u when firstMode != "inherit":
                        var set = editor.Find(u.StartOffset, u.SetClauses.FirstOrDefault()?.StartOffset ?? End(u), Is("SET"));
                        if (set is not null) editor.Before(set.Offset, "always", Zero(), anchorOffset: u.StartOffset);
                        List(u.SetClauses.Cast<TSqlFragment>().ToArray(), u.StartOffset, firstMode);
                        Clause(u.WhereClause, u.WhereClause?.SearchCondition, u.StartOffset, "never");
                        if (u.WhereClause?.SearchCondition is { } updateWhere) Boolean(updateWhere, u.StartOffset);
                        break;
                    case DeleteSpecification d when firstMode != "inherit":
                        Clause(d.WhereClause, d.WhereClause?.SearchCondition, d.StartOffset, "never");
                        if (d.WhereClause?.SearchCondition is { } deleteWhere) Boolean(deleteWhere, d.StartOffset);
                        break;
                    case FunctionCall call when Choice("functionArguments") != "inherit" && call.Parameters.Count > 0:
                        var functionMode = Choice("functionArguments");
                        var functionLong = functionMode == "always" || functionMode == "multiple" && call.Parameters.Count > 1
                            || functionMode == "ifLong" && editor.GetColumn(call.FunctionName.StartOffset)
                                + Flat(parsed, call.FunctionName.StartOffset, End(call)).Length > options.General.MaxLineWidth;
                        List(call.Parameters.Cast<TSqlFragment>().ToArray(), call.StartOffset, functionLong ? "always" : "never");
                        if (functionLong)
                        {
                            var functionOpen = editor.FindLast(call.StartOffset, call.Parameters[0].StartOffset, t => t.Text == "(");
                            var functionClose = editor.FindLast(call.Parameters[call.Parameters.Count - 1].StartOffset, End(call), t => t.Text == ")");
                            if (functionOpen is not null && functionClose is not null)
                            {
                                var functionColumn = editor.GetColumn(functionOpen.Offset);
                                editor.BeforeAtColumn(call.Parameters[0].StartOffset, functionColumn + options.Indent.Size);
                                for (var i = 1; i < call.Parameters.Count; i++)
                                {
                                    var comma = editor.Find(End(call.Parameters[i - 1]), call.Parameters[i].StartOffset, t => t.Text == ",");
                                    if (comma is not null) editor.BeforeAtColumn(comma.Offset, functionColumn + options.Indent.Size);
                                }
                                editor.BeforeAtColumn(functionClose.Offset, functionColumn);
                            }
                        }
                        break;
                    case InPredicate p when Choice("inValues") != "inherit" && p.Values.Count > 0:
                        List(p.Values.Cast<TSqlFragment>().ToArray(), p.StartOffset, Choice("inValues"), inlineSubsequent: true);
                        break;
                    case InsertSpecification i when Choice("parenthesesStyle") != "inherit":
                        if (i.Columns.Count > 0) BracketList(i, i.Columns.Cast<TSqlFragment>().ToArray(), i.StartOffset);
                        if (i.InsertSource is ValuesInsertSource values)
                            editor.BeforeAtColumn(values.StartOffset, editor.GetLineIndent(i.StartOffset));
                        break;
                    case RowValue r when Choice("parenthesesStyle") != "inherit" && r.ColumnValues.Count > 0:
                        var rowOwner = fragments.OfType<InsertSpecification>().Where(i => i.StartOffset < r.StartOffset && End(i) >= End(r))
                            .OrderBy(i => i.FragmentLength).FirstOrDefault();
                        BracketList(r, r.ColumnValues.Cast<TSqlFragment>().ToArray(), rowOwner?.StartOffset ?? r.StartOffset);
                        break;
                    case CommonTableExpression c when Choice("parenthesesStyle") != "inherit":
                        if (c.Columns.Count > 0) BracketList(c, c.Columns.Cast<TSqlFragment>().ToArray(), c.StartOffset);
                        Parentheses(c, c.QueryExpression);
                        break;
                    case QueryDerivedTable d when Choice("parenthesesStyle") != "inherit":
                        Parentheses(d, d.QueryExpression);
                        break;
                    case OverClause over when Choice("parenthesesStyle") != "inherit":
                        var overIndent = editor.GetLineIndent(over.StartOffset) + options.Indent.Size;
                        if (over.Partitions.Count > 0)
                        {
                            var partition = editor.Find(over.StartOffset, over.Partitions[0].StartOffset, Is("PARTITION"));
                            if (partition is not null) editor.BeforeAtColumn(partition.Offset, overIndent);
                        }
                        if (over.OrderByClause is { } windowOrder)
                            editor.BeforeAtColumn(windowOrder.StartOffset, overIndent);
                        var overClose = editor.FindLast(over.StartOffset, End(over), t => t.Text == ")");
                        if (overClose is not null) editor.BeforeAtColumn(overClose.Offset, overIndent - options.Indent.Size);
                        break;
                    case CaseExpression expression when Threshold("caseCompact").Enabled:
                        Case(expression);
                        break;
                    case CreateTableStatement t when Choice("parenthesesStyle") != "inherit" && t.Definition is { } definition:
                        BracketList(t, definition.ColumnDefinitions.Cast<TSqlFragment>().Concat(definition.TableConstraints).ToArray(), t.StartOffset);
                        break;
                    case ParenthesisExpression p when Choice("parenthesesStyle") != "inherit":
                        Parentheses(p, p.Expression);
                        break;
                    case BooleanParenthesisExpression p when Choice("parenthesesStyle") != "inherit":
                        Parentheses(p, p.Expression);
                        break;
                    case ScalarSubquery p when Choice("parenthesesStyle") != "inherit":
                        Parentheses(p, p.QueryExpression);
                        break;
                    case SetVariableStatement s when Flag("setValueOnNewLineIfLong") && s.Expression is { } value:
                        editor.Before(value.StartOffset, Fits(s, s.StartOffset) ? "never" : "always", indent, anchorOffset: s.StartOffset);
                        break;
                    case RestoreStatement r:
                        foreach (var move in FragmentsIn(r).OfType<MoveRestoreOption>())
                        {
                            if (Flag("restoreMoveOnNewLine")) editor.Before(move.StartOffset, "always", indent, anchorOffset: r.StartOffset);
                            if (Flag("restoreToOnNewLine"))
                            {
                                var to = editor.Find(move.StartOffset, End(move), Is("TO"));
                                if (to is not null) editor.Before(to.Offset, "always", new IndentRule(true, 2), anchorOffset: r.StartOffset);
                            }
                        }
                        break;
                }
            }
            return editor.Apply(cancellationToken);

            void Clause(TSqlFragment? clause, TSqlFragment? content, int anchor, string mode)
            {
                if (clause is null || content is null) return;
                editor.Before(clause.StartOffset, "always", Zero(), anchorOffset: anchor);
                editor.Before(content.StartOffset, mode);
            }
            void Join(JoinTableReference join, int anchor)
            {
                var keyword = editor.Find(End(join.FirstTableReference), join.SecondTableReference.StartOffset,
                    t => new[] { "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "JOIN" }.Contains(t.Text.ToUpperInvariant()));
                if (keyword is not null) editor.Before(keyword.Offset, "always", Zero(), anchorOffset: anchor);
                if (join is not QualifiedJoin qualified) return;
                var on = editor.Find(End(join.SecondTableReference), qualified.SearchCondition.StartOffset, Is("ON"));
                if (on is not null) editor.Before(on.Offset, "always", new IndentRule(true, 1), anchorOffset: anchor);
                editor.Before(qualified.SearchCondition.StartOffset, "never");
                Boolean(qualified.SearchCondition, anchor);
            }
            void Boolean(BooleanExpression expression, int anchor)
            {
                foreach (var b in FragmentsIn(expression).OfType<BooleanBinaryExpression>())
                {
                    var op = editor.Find(End(b.FirstExpression), b.SecondExpression.StartOffset,
                        t => Is("AND")(t) || Is("OR")(t));
                    if (op is null) continue;
                    editor.Before(op.Offset, "always", new IndentRule(true, 1), anchorOffset: anchor);
                    editor.Before(b.SecondExpression.StartOffset, "never");
                }
            }
            void List(TSqlFragment[] items, int anchor, string mode, bool force = false, bool inlineSubsequent = false)
            {
                if (items.Length == 0) return;
                var stacked = force || mode == "always" || mode == "multiple" && items.Length > 1
                    || mode == "ifLong" && !FitsRange(anchor, End(items[items.Length - 1]), anchor);
                var previous = parsed.Tokens.LastOrDefault(t => t.Offset < items[0].StartOffset
                    && t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile));
                editor.Before(items[0].StartOffset, stacked ? "always" : "never", stacked ? indent : null,
                    spaceMode: !stacked && previous?.Text == "(" ? "remove" : "inherit", anchorOffset: anchor);
                if (force) editor.BeforeAtColumn(items[0].StartOffset, editor.GetColumn(anchor) + 7);
                for (var i = 1; i < items.Length; i++)
                {
                    var comma = editor.Find(End(items[i - 1]), items[i].StartOffset, t => t.Text == ",");
                    if (comma is null) continue;
                    var leading = stacked && !inlineSubsequent && NativeRules.Get(options, "stackedList.commaPlacement").Choice == "leading";
                    editor.Before(comma.Offset, leading ? "always" : "never", leading ? indent : null, spaceMode: "remove", anchorOffset: anchor);
                    editor.After(comma.Offset, stacked && !inlineSubsequent && !leading ? "always" : "never",
                        stacked && !inlineSubsequent && !leading ? indent : null, "insert", anchor);
                    if (force && leading) editor.BeforeAtColumn(comma.Offset, editor.GetColumn(anchor) + 7);
                }
            }
            void Parentheses(TSqlFragment whole, TSqlFragment? content)
            {
                if (content is null) return;
                var open = editor.FindLast(whole.StartOffset, content.StartOffset, t => t.Text == "(");
                var close = editor.FindLast(End(content), End(whole), t => t.Text == ")");
                if (open is null || close is null) return;
                var threshold = Threshold("parenthesesCompact");
                var compact = Choice("parenthesesStyle") == "compact" || threshold.Enabled && Flat(parsed, content).Length < threshold.Value;
                if (compact)
                {
                    if (whole is CommonTableExpression) editor.Before(open.Offset, "never");
                    if (whole is QueryDerivedTable)
                    {
                        var owner = fragments.OfType<QuerySpecification>().Where(q => q.StartOffset < whole.StartOffset
                            && End(q) >= End(whole)).OrderBy(q => q.FragmentLength).FirstOrDefault();
                        var previous = parsed.Tokens.LastOrDefault(t => t.Offset < open.Offset
                            && t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile));
                        if (previous is not null && Is("APPLY")(previous)) editor.BeforeAtColumn(open.Offset, editor.GetLineIndent(owner?.StartOffset ?? whole.StartOffset));
                    }
                    editor.After(open.Offset, "never", spaceMode: "remove");
                    editor.Before(close.Offset, "never", spaceMode: "remove");
                }
                else
                {
                    var owner = fragments.OfType<QuerySpecification>().Where(q => q.StartOffset < whole.StartOffset
                        && End(q) >= End(whole)).OrderBy(q => q.FragmentLength).FirstOrDefault();
                    var anchor = owner?.StartOffset ?? whole.StartOffset;
                    editor.Before(open.Offset, "always", Zero(), anchorOffset: anchor);
                    editor.After(open.Offset, "always", indent, anchorOffset: open.Offset);
                    editor.Before(close.Offset, "always", Zero(), anchorOffset: open.Offset);
                }
            }
            void Case(CaseExpression expression)
            {
                var column = editor.GetColumn(expression.StartOffset);
                IEnumerable<WhenClause> clauses = expression switch
                {
                    SimpleCaseExpression simple => simple.WhenClauses,
                    SearchedCaseExpression searched => searched.WhenClauses,
                    _ => Array.Empty<WhenClause>()
                };
                foreach (var when in clauses)
                {
                    editor.BeforeAtColumn(when.StartOffset, column + options.Indent.Size);
                    if (when.ThenExpression is { } value) editor.BeforeAtColumn(value.StartOffset, column + options.Indent.Size * 2);
                    if (when is SearchedWhenClause searched)
                        foreach (var b in FragmentsIn(searched.WhenExpression).OfType<BooleanBinaryExpression>())
                        {
                            var op = editor.Find(End(b.FirstExpression), b.SecondExpression.StartOffset,
                                t => Is("AND")(t) || Is("OR")(t));
                            if (op is not null) editor.BeforeAtColumn(op.Offset, column + options.Indent.Size * 2);
                        }
                }
                if (expression.ElseExpression is { } otherwise)
                {
                    var elseToken = editor.FindLast(expression.StartOffset, otherwise.StartOffset, Is("ELSE"));
                    if (elseToken is not null) editor.BeforeAtColumn(elseToken.Offset, column + options.Indent.Size);
                    editor.BeforeAtColumn(otherwise.StartOffset, column + options.Indent.Size * 2);
                }
                var end = editor.FindLast(expression.StartOffset, End(expression), Is("END"));
                if (end is not null) editor.BeforeAtColumn(end.Offset, column);
            }
            void BracketList(TSqlFragment whole, TSqlFragment[] items, int anchor)
            {
                if (items.Length == 0) return;
                var open = editor.FindLast(whole.StartOffset, items[0].StartOffset, t => t.Text == "(");
                var close = editor.Find(End(items[items.Length - 1]), End(whole), t => t.Text == ")");
                if (open is null || close is null) return;
                var threshold = Threshold("parenthesesCompact");
                var compact = Choice("parenthesesStyle") == "compact" || threshold.Enabled
                    && Flat(parsed, items[0].StartOffset, End(items[items.Length - 1])).Length < threshold.Value;
                if (whole is RowValue)
                {
                    var previous = parsed.Tokens.LastOrDefault(t => t.Offset < open.Offset
                        && t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile));
                    if (previous is not null && Is("VALUES")(previous)) editor.BeforeAtColumn(open.Offset, editor.GetLineIndent(anchor));
                    else if (previous?.Text == ",")
                    {
                        editor.BeforeAtColumn(previous.Offset, editor.GetLineIndent(anchor));
                        editor.Before(open.Offset, "never");
                    }
                }
                else editor.Before(open.Offset, compact ? "never" : "always", compact ? null : Zero(), "insert", anchor);
                List(items, anchor, compact ? "never" : "always");
                editor.After(open.Offset, compact ? "never" : "always", compact ? null : indent, "remove", anchor);
                editor.Before(close.Offset, compact ? "never" : "always", compact ? null : Zero(), "remove", anchor);
            }
            bool Fits(TSqlFragment f, int anchor) => FitsRange(f.StartOffset, End(f), anchor);
            bool FitsRange(int start, int end, int anchor) => editor.GetLineIndent(anchor)
                + Flat(parsed, start, end).Length <= options.General.MaxLineWidth;
        }

        string Compact(SqlParseResult parsed)
        {
            var edits = new List<TextEdit>();
            var lastEnd = -1;
            var fragments = Fragments(parsed).ToArray();
            foreach (var f in fragments.OrderBy(f => f.StartOffset).ThenByDescending(f => f.FragmentLength))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (f.StartOffset < lastEnd || f.FragmentLength <= 0) continue;
                string? key = f switch
                {
                    SelectStatement { WithCtesAndXmlNamespaces: null } or InsertStatement or UpdateStatement or DeleteStatement => "dmlCompact",
                    CaseExpression => "caseCompact",
                    ScalarSubquery or QueryDerivedTable => "subqueryCompact",
                    QuerySpecification q when fragments.Any(w => w is CommonTableExpression && w.StartOffset < q.StartOffset && End(w) >= End(q)) => "parenthesesCompact",
                    ParenthesisExpression or BooleanParenthesisExpression => "parenthesesCompact",
                    OverClause => "parenthesesCompact",
                    CreateTableStatement or DeclareVariableStatement => "ddlCompact",
                    _ => null
                };
                if (key is null && f is FunctionCall && Choice("functionArguments") == "ifLong") key = "function";
                if (key is null) continue;
                if (HasComment(parsed, f.StartOffset, End(f)) || TrailingComment(parsed, f)) continue;
                var flat = Flat(parsed, f);
                if (key == "function")
                {
                    if (End(f) - f.StartOffset == flat.Length || f.StartOffset - LineStart(parsed.Source, f.StartOffset)
                        + flat.Length > options.General.MaxLineWidth) continue;
                    edits.Add(new TextEdit(new SqlTextSpan(f.StartOffset, f.FragmentLength), flat));
                    lastEnd = End(f);
                    continue;
                }
                var threshold = Threshold(key);
                if (!threshold.Enabled) continue;
                if (flat.Length >= threshold.Value || LineIndent(parsed.Source, f.StartOffset, options.Indent.Size)
                    + flat.Length > options.General.MaxLineWidth) continue;
                if (flat == parsed.Source.Substring(f.StartOffset, f.FragmentLength)) continue;
                edits.Add(new TextEdit(new SqlTextSpan(f.StartOffset, f.FragmentLength), flat));
                lastEnd = End(f);
            }
            return KeywordCasing.Apply(parsed.Source, edits, cancellationToken);
        }

        string Align(SqlParseResult parsed, bool definitionsOnly)
        {
            var edits = new Dictionary<int, TextEdit>();
            foreach (var f in definitionsOnly ? Fragments(parsed) : Array.Empty<TSqlFragment>())
            {
                if (Flag("alignDeclarationValues") && f is DeclareVariableStatement d) Definitions(d.Declarations.Cast<TSqlFragment>().ToArray());
                if (Flag("alignDeclarationValues") && f is ProcedureStatementBody p) Definitions(p.Parameters.Cast<TSqlFragment>().ToArray());
                if (Flag("alignDdlTypes") && f is TableDefinition table) Definitions(table.ColumnDefinitions.Cast<TSqlFragment>().ToArray());
            }
            if (!definitionsOnly && (Flag("alignListComments") || Flag("alignCommentGroups")))
            {
                var comments = parsed.Tokens.Where(t => t.TokenType == TSqlTokenType.SingleLineComment).ToArray();
                var group = new List<TSqlParserToken>();
                foreach (var comment in comments)
                {
                    if (group.Count > 0 && LineNumber(parsed.Source, comment.Offset) != LineNumber(parsed.Source, group[group.Count - 1].Offset) + 1)
                    { AlignComments(group); group.Clear(); }
                    group.Add(comment);
                }
                AlignComments(group);
            }
            return KeywordCasing.Apply(parsed.Source, edits.Values.OrderBy(e => e.Span.StartOffset).ToArray(), cancellationToken);

            void Definitions(TSqlFragment[] definitions)
            {
                var parts = definitions.Select(f => f switch
                {
                    DeclareVariableElement v => (Name: (TSqlFragment?)v.VariableName, Type: (TSqlFragment?)v.DataType, Value: (TSqlFragment?)v.Value),
                    ColumnDefinition c => (Name: (TSqlFragment?)c.ColumnIdentifier, Type: (TSqlFragment?)c.DataType, Value: (TSqlFragment?)null),
                    _ => (Name: (TSqlFragment?)null, Type: (TSqlFragment?)null, Value: (TSqlFragment?)null)
                }).Where(p => p.Name is not null && p.Type is not null).ToArray();
                if (parts.Length < 2 || parts.Select(p => LineNumber(parsed.Source, p.Name!.StartOffset)).Distinct().Count() != parts.Length) return;
                var nameWidth = parts.Max(p => p.Name!.FragmentLength + (LeadingComma(p.Name.StartOffset) ? 2 : 0));
                var typeWidth = parts.Max(p => p.Type!.FragmentLength);
                foreach (var p in parts)
                {
                    Gap(End(p.Name!), p.Type!.StartOffset, new string(' ', nameWidth - p.Name!.FragmentLength - (LeadingComma(p.Name.StartOffset) ? 2 : 0) + 1));
                    if (p.Value is null) continue;
                    var equals = parsed.Tokens.FirstOrDefault(t => t.Offset >= End(p.Type) && t.Offset < p.Value.StartOffset && t.Text == "=");
                    if (equals is not null) Gap(End(p.Type), equals.Offset, new string(' ', typeWidth - p.Type.FragmentLength + 1));
                }
            }
            bool LeadingComma(int offset) => parsed.Source.Substring(LineStart(parsed.Source, offset), offset - LineStart(parsed.Source, offset)).Trim() == ",";
            void Gap(int start, int end, string replacement)
            {
                var gap = parsed.Source.Substring(start, end - start);
                if (gap.All(c => c is ' ' or '\t')) edits[start] = new TextEdit(new SqlTextSpan(start, gap.Length), replacement);
            }
            void AlignComments(List<TSqlParserToken> group)
            {
                if (group.Count < 2) return;
                var width = group.Max(t => t.Offset - LineStart(parsed.Source, t.Offset));
                foreach (var t in group)
                {
                    var lineStart = LineStart(parsed.Source, t.Offset);
                    var start = t.Offset;
                    while (start > lineStart && parsed.Source[start - 1] is ' ' or '\t') start--;
                    if (width + t.Text.Length > options.General.MaxLineWidth) return;
                    Gap(start, t.Offset, new string(' ', width - (start - lineStart)));
                }
            }
        }

        string Separate(SqlParseResult parsed)
        {
            var count = NativeRules.Get(options, "layout.blankLinesBetweenStatements").Integer;
            var batches = NativeRules.Get(options, "layout.blankLinesAfterBatch").Integer;
            if (count < 0 && batches < 0) return parsed.Source;
            var edits = new Dictionary<int, TextEdit>();
            if (count >= 0)
                foreach (var f in Fragments(parsed))
                {
                    IEnumerable<TSqlStatement>? statements = f switch
                    {
                        TSqlBatch b => b.Statements,
                        StatementList l => l.Statements,
                        _ => null
                    };
                    if (statements is null) continue;
                    var list = statements.Where(s => s.FragmentLength > 0).ToArray();
                    for (var i = 1; i < list.Length; i++) SetGap(End(list[i - 1]), list[i].StartOffset, count);
                }
            if (batches >= 0)
            {
                var tokens = parsed.Tokens.Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).ToArray();
                for (var i = 0; i + 1 < tokens.Length; i++)
                    if (tokens[i].TokenType == TSqlTokenType.Go) SetGap(tokens[i].Offset + tokens[i].Text.Length, tokens[i + 1].Offset, batches);
            }
            return KeywordCasing.Apply(parsed.Source, edits.Values.OrderBy(e => e.Span.StartOffset).ToArray(), cancellationToken);
            void SetGap(int start, int end, int blankLines)
            {
                if (end < start) return;
                var gap = parsed.Source.Substring(start, end - start);
                if (!gap.All(char.IsWhiteSpace)) return;
                edits[start] = new TextEdit(new SqlTextSpan(start, end - start), string.Concat(Enumerable.Repeat(Newline(options), blankLines + 1))
                    + new string(' ', LineIndent(parsed.Source, end, options.Indent.Size)));
            }
        }

        string Whitespace(SqlParseResult parsed)
        {
            var edits = parsed.Tokens.Where(t => t.TokenType == TSqlTokenType.WhiteSpace)
                .Select(t => new TextEdit(new SqlTextSpan(t.Offset, t.Text.Length), Regex.Replace(t.Text, @"\r\n|\r|\n", Newline(options))))
                .Where(e => parsed.Source.Substring(e.Span.StartOffset, e.Span.Length) != e.NewText).ToArray();
            var result = KeywordCasing.Apply(parsed.Source, edits, cancellationToken);
            return options.General.FinalNewline ? result.TrimEnd('\r', '\n') + Newline(options) : result;
        }

        string Choice(string key) => NativeRules.Get(options, "layout." + key).Choice;
        bool Flag(string key) => NativeRules.Get(options, "layout." + key).Boolean;
        ThresholdRule Threshold(string key) => NativeRules.Get(options, "layout." + key).Threshold;
        IEnumerable<TSqlFragment> Fragments(SqlParseResult p) => new SqlFragmentWalker().Walk(p.Root!, cancellationToken);
        IEnumerable<TSqlFragment> FragmentsIn(TSqlFragment f) => new SqlFragmentWalker().Walk(f, cancellationToken);
    }

    private static string Flat(SqlParseResult parsed, TSqlFragment f) => Flat(parsed, f.StartOffset, End(f));

    private static string RestoreDirectives(string original, string formatted, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        var before = parser.Parse(original, dialect, cancellationToken);
        var after = parser.Parse(formatted, dialect, cancellationToken);
        var oldMarkers = Markers(before).ToArray();
        var newMarkers = Markers(after).ToArray();
        if (oldMarkers.Length == 0 || oldMarkers.Length != newMarkers.Length) return formatted;
        var edits = new List<TextEdit>();
        for (var i = 0; i < oldMarkers.Length; i++)
        {
            if (!IsOff(oldMarkers[i])) continue;
            var start = oldMarkers[i].Offset + oldMarkers[i].Text.Length;
            var newStart = newMarkers[i].Offset + newMarkers[i].Text.Length;
            var j = i + 1;
            while (j < oldMarkers.Length && IsOff(oldMarkers[j])) j++;
            var end = j < oldMarkers.Length ? oldMarkers[j].Offset : original.Length;
            var newEnd = j < newMarkers.Length ? newMarkers[j].Offset : formatted.Length;
            edits.Add(new TextEdit(new SqlTextSpan(newStart, newEnd - newStart), original.Substring(start, end - start)));
            i = j;
        }
        var restored = KeywordCasing.Apply(formatted, edits, cancellationToken);
        return parser.Parse(restored, dialect, cancellationToken).ParseSucceeded ? restored : original;
        static IEnumerable<TSqlParserToken> Markers(SqlParseResult p) => p.Tokens.Where(t => t.TokenType == TSqlTokenType.SingleLineComment
            && Regex.IsMatch(t.Text, @"^--\s*SQL Prompt formatting (off|on)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        static bool IsOff(TSqlParserToken t) => t.Text.TrimEnd().EndsWith("off", StringComparison.OrdinalIgnoreCase);
    }
    private static string Flat(SqlParseResult parsed, int start, int end)
    {
        // Work on whitespace tokens only: never replace whitespace inside a literal or comment.
        var tokens = parsed.Tokens.Where(t => t.Offset >= start && t.Offset < end
            && t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile)).ToArray();
        var text = new System.Text.StringBuilder();
        TSqlParserToken? previous = null;
        foreach (var token in tokens)
        {
            if (previous is not null && token.Offset > previous.Offset + previous.Text.Length
                && token.Text is not ("," or ";" or ")") && previous.Text != "(") text.Append(' ');
            text.Append(token.Text);
            previous = token;
        }
        return text.ToString();
    }
    private static bool HasComment(SqlParseResult p, int start, int end) => p.Tokens.Any(t => t.Offset >= start && t.Offset < end
        && t.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment);
    private static bool TrailingComment(SqlParseResult p, TSqlFragment f)
    {
        var next = p.Tokens.FirstOrDefault(t => t.Offset >= End(f) && t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile));
        return next?.TokenType == TSqlTokenType.SingleLineComment && LineStart(p.Source, next.Offset) <= End(f);
    }
    private static IndentRule Zero() => new(true, 0);
    private static Func<TSqlParserToken, bool> Is(string word) => t => t.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
    private static int End(TSqlFragment f) => f.StartOffset + f.FragmentLength;
    private static int LineStart(string text, int offset) => offset == 0 ? 0 : text.LastIndexOfAny(new[] { '\r', '\n' }, offset - 1) + 1;
    private static int LineIndent(string text, int offset, int size) => text.Substring(LineStart(text, offset), offset - LineStart(text, offset))
        .TakeWhile(c => c is ' ' or '\t').Sum(c => c == '\t' ? size : 1);
    private static int LineNumber(string text, int offset) => text.Take(offset).Count(c => c == '\n');
    private static string Newline(FormattingOptions o) => o.General.LineEnding switch { DocLineEnding.CrLf => "\r\n", DocLineEnding.Cr => "\r", _ => "\n" };
}
