using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Core.Formatting.Builders;

/// <summary>Conservative layout for simple control flow and stored-code bodies.</summary>
internal sealed class StoredCodeDocBuilder : ISqlFragmentDocBuilder
{
    private static readonly RegexOptions IgnoreCase = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public bool Applied { get; private set; }

    public bool CanBuild(TSqlFragment fragment) => fragment is BeginEndBlockStatement
        or IfStatement or WhileStatement or TryCatchStatement or DeclareVariableStatement
        or SetVariableStatement or ThrowStatement or CreateProcedureStatement
        or AlterProcedureStatement or CreateFunctionStatement or AlterFunctionStatement
        or CreateViewStatement or AlterViewStatement;

    public Doc Build(TSqlFragment fragment, SqlDocBuilderContext context)
    {
        Doc? result = fragment switch
        {
            BeginEndBlockStatement block => BuildBlock(block, context),
            IfStatement conditional => BuildIf(conditional, context),
            WhileStatement loop => BuildWhile(loop, context),
            TryCatchStatement handler => BuildTryCatch(handler, context),
            DeclareVariableStatement declaration => BuildDeclare(declaration, context),
            SetVariableStatement assignment => BuildSet(assignment, context),
            ThrowStatement exception => BuildThrow(exception, context),
            CreateProcedureStatement procedure => BuildModule(procedure, procedure.StatementList, context),
            AlterProcedureStatement procedure => BuildModule(procedure, procedure.StatementList, context),
            CreateFunctionStatement function => BuildModule(function, function.StatementList, context),
            AlterFunctionStatement function => BuildModule(function, function.StatementList, context),
            CreateViewStatement view => BuildView(view, view.SelectStatement, context),
            AlterViewStatement view => BuildView(view, view.SelectStatement, context),
            _ => null
        };
        if (result is null) return new TextDoc(context.GetOriginalText(fragment));
        Applied = true;
        return result;
    }

    private static Doc? BuildBlock(BeginEndBlockStatement block, SqlDocBuilderContext context)
    {
        var statements = block.StatementList?.Statements;
        if (statements is null || statements.Count == 0) return null;
        var first = statements[0];
        var last = statements[statements.Count - 1];
        if (!Matches(Between(context, block.StartOffset, first.StartOffset), @"^\s*BEGIN\s*$")
            || !Matches(Between(context, End(last), End(block)), @"^\s*END\s*;?\s*$"))
            return null;
        var body = BuildStatementList(statements, context);
        if (body is null) return null;
        var suffix = Between(context, End(last), End(block));
        return Join(new TextDoc("BEGIN"), new IndentDoc(1, Join(HardLineDoc.Instance, body)),
            HardLineDoc.Instance, new TextDoc(suffix.Contains(';') ? "END;" : "END"));
    }

    private static Doc? BuildIf(IfStatement statement, SqlDocBuilderContext context)
    {
        if (statement.Predicate is null || statement.ThenStatement is null
            || !Matches(Between(context, statement.StartOffset, statement.Predicate.StartOffset), @"^\s*IF\s+$")
            || !White(Between(context, End(statement.Predicate), statement.ThenStatement.StartOffset)))
            return null;
        var parts = new List<Doc> { new TextDoc("IF " + context.GetOriginalText(statement.Predicate).Trim()),
            Branch(statement.ThenStatement, context) };
        if (statement.ElseStatement is { } alternative)
        {
            if (!Matches(Between(context, End(statement.ThenStatement), alternative.StartOffset), @"^\s*ELSE\s*$"))
                return null;
            parts.Add(HardLineDoc.Instance);
            parts.Add(new TextDoc("ELSE"));
            parts.Add(Branch(alternative, context));
        }
        else if (!White(Between(context, End(statement.ThenStatement), End(statement)))) return null;
        return Join(parts);
    }

    private static Doc? BuildWhile(WhileStatement statement, SqlDocBuilderContext context)
    {
        if (statement.Predicate is null || statement.Statement is null
            || !Matches(Between(context, statement.StartOffset, statement.Predicate.StartOffset), @"^\s*WHILE\s+$")
            || !White(Between(context, End(statement.Predicate), statement.Statement.StartOffset))
            || !White(Between(context, End(statement.Statement), End(statement)))) return null;
        return Join(new TextDoc("WHILE " + context.GetOriginalText(statement.Predicate).Trim()),
            Branch(statement.Statement, context));
    }

    private static Doc? BuildTryCatch(TryCatchStatement statement, SqlDocBuilderContext context)
    {
        var tries = statement.TryStatements?.Statements;
        var catches = statement.CatchStatements?.Statements;
        if (tries is null || catches is null || tries.Count == 0 || catches.Count == 0
            || !Matches(Between(context, statement.StartOffset, tries[0].StartOffset), @"^\s*BEGIN\s+TRY\s*$")
            || !Matches(Between(context, End(tries[tries.Count - 1]), catches[0].StartOffset),
                @"^\s*END\s+TRY\s+BEGIN\s+CATCH\s*$")
            || !Matches(Between(context, End(catches[catches.Count - 1]), End(statement)),
                @"^\s*END\s+CATCH\s*;?\s*$")) return null;
        var tryBody = BuildStatementList(tries, context);
        var catchBody = BuildStatementList(catches, context);
        if (tryBody is null || catchBody is null) return null;
        return Join(new TextDoc("BEGIN TRY"), new IndentDoc(1, Join(HardLineDoc.Instance, tryBody)),
            HardLineDoc.Instance, new TextDoc("END TRY"), HardLineDoc.Instance,
            new TextDoc("BEGIN CATCH"), new IndentDoc(1, Join(HardLineDoc.Instance, catchBody)),
            HardLineDoc.Instance, new TextDoc("END CATCH"));
    }

    private static Doc? BuildDeclare(DeclareVariableStatement statement, SqlDocBuilderContext context)
    {
        var variables = statement.Declarations;
        if (variables.Count == 0
            || !Matches(Between(context, statement.StartOffset, variables[0].StartOffset), @"^\s*DECLARE\s+$")
            || !Matches(Between(context, End(variables[variables.Count - 1]), End(statement)), @"^\s*;?\s*$"))
            return null;
        var parts = new List<Doc> { new TextDoc("DECLARE ") };
        for (int index = 0; index < variables.Count; index++)
        {
            if (index > 0)
            {
                if (!Matches(Between(context, End(variables[index - 1]), variables[index].StartOffset), @"^\s*,\s*$"))
                    return null;
                parts.Add(new TextDoc(","));
                parts.Add(new IndentDoc(1, Join(HardLineDoc.Instance,
                    new TextDoc(context.GetOriginalText(variables[index]).Trim()))));
                continue;
            }
            parts.Add(new TextDoc(context.GetOriginalText(variables[index]).Trim()));
        }
        if (Between(context, End(variables[variables.Count - 1]), End(statement)).Contains(';'))
            parts.Add(new TextDoc(";"));
        return Join(parts);
    }

    private static Doc? BuildSet(SetVariableStatement statement, SqlDocBuilderContext context)
    {
        if (statement.Variable is null || statement.Expression is null
            || !Matches(Between(context, statement.StartOffset, statement.Variable.StartOffset), @"^\s*SET\s+$")
            || !Matches(Between(context, End(statement.Variable), statement.Expression.StartOffset), @"^\s*=\s*$")
            || !Matches(Between(context, End(statement.Expression), End(statement)), @"^\s*;?\s*$"))
            return null;
        var tail = Between(context, End(statement.Expression), End(statement));
        return new TextDoc("SET " + context.GetOriginalText(statement.Variable).Trim() + " = "
            + context.GetOriginalText(statement.Expression).Trim() + (tail.Contains(';') ? ";" : ""));
    }

    private static Doc? BuildThrow(ThrowStatement statement, SqlDocBuilderContext context)
    {
        if (statement.ErrorNumber is null)
            return Matches(context.GetOriginalText(statement), @"^\s*THROW\s*;?\s*$")
                ? new TextDoc(context.GetOriginalText(statement).Contains(';') ? "THROW;" : "THROW") : null;
        if (statement.Message is null || statement.State is null
            || !Matches(Between(context, statement.StartOffset, statement.ErrorNumber.StartOffset), @"^\s*THROW\s+$")
            || !Matches(Between(context, End(statement.ErrorNumber), statement.Message.StartOffset), @"^\s*,\s*$")
            || !Matches(Between(context, End(statement.Message), statement.State.StartOffset), @"^\s*,\s*$")
            || !Matches(Between(context, End(statement.State), End(statement)), @"^\s*;?\s*$")) return null;
        var tail = Between(context, End(statement.State), End(statement));
        return new TextDoc("THROW " + context.GetOriginalText(statement.ErrorNumber).Trim() + ", "
            + context.GetOriginalText(statement.Message).Trim() + ", "
            + context.GetOriginalText(statement.State).Trim() + (tail.Contains(';') ? ";" : ""));
    }

    private static Doc? BuildModule(TSqlStatement module, StatementList? list, SqlDocBuilderContext context)
    {
        var statements = list?.Statements;
        if (statements is null || statements.Count == 0) return null;
        var header = Between(context, module.StartOffset, statements[0].StartOffset);
        if (header.Contains("--") || header.Contains("/*")
            || !Matches(header, @"^\s*(CREATE|ALTER)\s+(PROCEDURE|PROC|FUNCTION)\b[\s\S]*\bAS\s*$")
            || !White(Between(context, End(statements[statements.Count - 1]), End(module)))) return null;
        var body = BuildStatementList(statements, context);
        if (body is null) return null;
        var split = Regex.Match(header, @"\s+AS\s*$", IgnoreCase);
        if (!split.Success) return null;
        var headerText = header.Substring(0, split.Index).Trim();
        return Join(new TextDoc(headerText), HardLineDoc.Instance, new TextDoc("AS"),
            statements[0] is BeginEndBlockStatement
                ? Join(HardLineDoc.Instance, body)
                : new IndentDoc(1, Join(HardLineDoc.Instance, body)));
    }

    private static Doc? BuildView(TSqlStatement view, SelectStatement? query, SqlDocBuilderContext context)
    {
        if (query is null) return null;
        var header = Between(context, view.StartOffset, query.StartOffset);
        if (header.Contains("--") || header.Contains("/*")
            || !Matches(header, @"^\s*(CREATE|ALTER)\s+VIEW\b[\s\S]*\bAS\s*$")
            || !Matches(Between(context, End(query), End(view)), @"^\s*;?\s*$")) return null;
        var split = Regex.Match(header, @"\s+AS\s*$", IgnoreCase);
        if (!split.Success) return null;
        return Join(new TextDoc(header.Substring(0, split.Index).Trim()), HardLineDoc.Instance,
            new TextDoc("AS"), HardLineDoc.Instance, context.BuildFragment(query),
            new TextDoc(Between(context, End(query), End(view)).Contains(';') ? ";" : ""));
    }

    private static Doc? BuildStatementList(IList<TSqlStatement> statements, SqlDocBuilderContext context)
    {
        var parts = new List<Doc>();
        for (int index = 0; index < statements.Count; index++)
        {
            if (index > 0)
            {
                if (!White(Between(context, End(statements[index - 1]), statements[index].StartOffset))) return null;
                parts.Add(HardLineDoc.Instance);
            }
            parts.Add(context.BuildFragment(statements[index]));
        }
        return Join(parts);
    }

    private static Doc Branch(TSqlStatement statement, SqlDocBuilderContext context) =>
        statement is BeginEndBlockStatement
            ? Join(HardLineDoc.Instance, context.BuildFragment(statement))
            : new IndentDoc(1, Join(HardLineDoc.Instance, context.BuildFragment(statement)));

    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;

    private static string Between(SqlDocBuilderContext context, int start, int end) =>
        context.ParseResult.Source.Substring(start, end - start);

    private static bool White(string text) => string.IsNullOrWhiteSpace(text);

    private static bool Matches(string text, string pattern) => Regex.IsMatch(text, pattern, IgnoreCase);

    private static Doc Join(params Doc[] parts) => new ConcatDoc(parts);

    private static Doc Join(IReadOnlyList<Doc> parts) => new ConcatDoc(parts);
}
