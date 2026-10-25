using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Navigation;

/// <summary>
/// Builds a navigation tree of the script: batches with modules, statements,
/// CTEs and variable declarations. Node spans point at names for quick navigation.
/// </summary>
public sealed class ScriptOutlineBuilder
{
    public ScriptOutlineNode? Build(SqlParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        if (parseResult.Root is not TSqlScript script)
        {
            return null;
        }

        var root = new ScriptOutlineNode(
            ScriptOutlineNodeKind.Script, "Script", new SqlTextSpan(0, parseResult.Source.Length));
        for (var index = 0; index < script.Batches.Count; index++)
        {
            var batch = script.Batches[index];
            var batchNode = new ScriptOutlineNode(
                ScriptOutlineNodeKind.Batch, $"Batch {index + 1}",
                new SqlTextSpan(batch.StartOffset, batch.FragmentLength));
            foreach (var statement in batch.Statements)
            {
                AddStatement(batchNode, statement);
            }

            root.Add(batchNode);
        }

        return root;
    }

    private static void AddStatement(ScriptOutlineNode parent, TSqlStatement statement)
    {
        if (TryCreateModuleNode(statement) is { } module)
        {
            if (module.StatementList is { } body)
            {
                foreach (var inner in body.Statements)
                {
                    AddStatement(module.Node, inner);
                }
            }

            parent.Add(module.Node);
            return;
        }

        if (statement is DeclareVariableStatement declaration)
        {
            var node = new ScriptOutlineNode(ScriptOutlineNodeKind.Statement, "DECLARE",
                new SqlTextSpan(statement.StartOffset, statement.FragmentLength));
            foreach (var element in declaration.Declarations)
            {
                if (element.VariableName is { } variableName)
                {
                    node.Add(new ScriptOutlineNode(ScriptOutlineNodeKind.Variable,
                        variableName.Value,
                        new SqlTextSpan(variableName.StartOffset, variableName.FragmentLength)));
                }
            }

            parent.Add(node);
            return;
        }

        var statementNode = new ScriptOutlineNode(ScriptOutlineNodeKind.Statement,
            StatementTitle(statement),
            new SqlTextSpan(statement.StartOffset, statement.FragmentLength));
        if (statement is StatementWithCtesAndXmlNamespaces { WithCtesAndXmlNamespaces: { } ctes } &&
            ctes.CommonTableExpressions.Count > 0)
        {
            foreach (var expression in ctes.CommonTableExpressions)
            {
                statementNode.Add(new ScriptOutlineNode(ScriptOutlineNodeKind.Cte,
                    "CTE " + expression.ExpressionName.Value,
                    new SqlTextSpan(expression.ExpressionName.StartOffset, expression.ExpressionName.FragmentLength)));
            }
        }

        if (statement is BeginEndBlockStatement block)
        {
            foreach (var inner in block.StatementList.Statements)
            {
                AddStatement(statementNode, inner);
            }
        }
        else if (statement is IfStatement conditional)
        {
            AddStatement(statementNode, conditional.ThenStatement);
            if (conditional.ElseStatement is { } elseStatement)
            {
                AddStatement(statementNode, elseStatement);
            }
        }
        else if (statement is WhileStatement loop)
        {
            AddStatement(statementNode, loop.Statement);
        }

        parent.Add(statementNode);
    }

    private static (ScriptOutlineNode Node, StatementList? StatementList)? TryCreateModuleNode(TSqlStatement statement)
    {
        switch (statement)
        {
            case CreateProcedureStatement create:
                return Module("PROCEDURE", create.ProcedureReference?.Name,
                    create.StatementList, create.StartOffset, create.FragmentLength);
            case AlterProcedureStatement alter:
                return Module("PROCEDURE", alter.ProcedureReference?.Name,
                    alter.StatementList, alter.StartOffset, alter.FragmentLength);
            case CreateOrAlterProcedureStatement createOrAlter:
                return Module("PROCEDURE", createOrAlter.ProcedureReference?.Name,
                    createOrAlter.StatementList, createOrAlter.StartOffset, createOrAlter.FragmentLength);
            case CreateFunctionStatement createFunction:
                return Module("FUNCTION", createFunction.Name,
                    createFunction.StatementList, createFunction.StartOffset, createFunction.FragmentLength);
            case AlterFunctionStatement alterFunction:
                return Module("FUNCTION", alterFunction.Name,
                    alterFunction.StatementList, alterFunction.StartOffset, alterFunction.FragmentLength);
            case CreateOrAlterFunctionStatement createOrAlterFunction:
                return Module("FUNCTION", createOrAlterFunction.Name,
                    createOrAlterFunction.StatementList, createOrAlterFunction.StartOffset, createOrAlterFunction.FragmentLength);
            case CreateViewStatement view:
                return Module("VIEW", view.SchemaObjectName,
                    null, view.StartOffset, view.FragmentLength);
            case AlterViewStatement alterView:
                return Module("VIEW", alterView.SchemaObjectName,
                    null, alterView.StartOffset, alterView.FragmentLength);
            case CreateOrAlterViewStatement createOrAlterView:
                return Module("VIEW", createOrAlterView.SchemaObjectName,
                    null, createOrAlterView.StartOffset, createOrAlterView.FragmentLength);
            case CreateTriggerStatement trigger:
                return Module("TRIGGER", trigger.Name,
                    null, trigger.StartOffset, trigger.FragmentLength);
            default:
                return null;
        }
    }

    private static (ScriptOutlineNode Node, StatementList? StatementList)? Module(
        string kind, SchemaObjectName? name, StatementList? statementList, int start, int length)
    {
        var span = NameSpan(name) ?? new SqlTextSpan(start, length);
        return (new ScriptOutlineNode(ScriptOutlineNodeKind.Module, $"{kind} {SchemaName(name)}", span),
            statementList);
    }

    private static SqlTextSpan? NameSpan(SchemaObjectName? name)
    {
        if (name is null || name.Identifiers.Count == 0)
        {
            return null;
        }

        var first = name.Identifiers[0];
        var last = name.Identifiers[name.Identifiers.Count - 1];
        return new SqlTextSpan(first.StartOffset, last.StartOffset + last.FragmentLength - first.StartOffset);
    }

    private static string SchemaName(SchemaObjectName? name)
    {
        if (name is null)
        {
            return "?";
        }

        var parts = new List<string>();
        for (var index = 0; index < name.Identifiers.Count; index++)
        {
            parts.Add(name.Identifiers[index].Value);
        }

        return string.Join(".", parts);
    }

    private static string StatementTitle(TSqlStatement statement) => statement switch
    {
        SelectStatement => "SELECT",
        InsertStatement => "INSERT",
        UpdateStatement => "UPDATE",
        DeleteStatement => "DELETE",
        MergeStatement => "MERGE",
        SetVariableStatement => "SET",
        ExecuteStatement => "EXEC",
        IfStatement => "IF",
        WhileStatement => "WHILE",
        BeginEndBlockStatement => "BEGIN...END",
        BeginTransactionStatement => "BEGIN TRANSACTION",
        CommitTransactionStatement => "COMMIT",
        RollbackTransactionStatement => "ROLLBACK",
        PrintStatement => "PRINT",
        ReturnStatement => "RETURN",
        DeclareCursorStatement => "DECLARE CURSOR",
        UseStatement => "USE",
        _ => statement.GetType().Name.EndsWith("Statement", StringComparison.Ordinal)
            ? statement.GetType().Name.Substring(0, statement.GetType().Name.Length - "Statement".Length)
                .ToUpperInvariant()
            : statement.GetType().Name.ToUpperInvariant()
    };
}
