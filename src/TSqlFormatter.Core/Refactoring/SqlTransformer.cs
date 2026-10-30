using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Refactoring;

/// <summary>
/// Rewrites the statement under an offset, regenerates it from a modified AST and
/// formats the whole document with the given profile options.
/// </summary>
public static class SqlTransformer
{
    public const string ReturnCodeVariable = "@returnCode";

    public static SqlTransformationResult Transform(
        SqlTransformationKind kind,
        string source,
        int offset,
        FormattingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (offset < 0 || offset > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var parseResult = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto, cancellationToken);
        if (parseResult.Root is null || parseResult.Diagnostics.Count > 0)
        {
            return SqlTransformationResult.Failed("SQL could not be parsed; the transformation was not applied.");
        }

        var statement = FindStatementAt(parseResult, offset);
        if (statement is null)
        {
            return SqlTransformationResult.Failed("No statement is found under the caret.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string statementText;
        try
        {
            statementText = kind switch
            {
                SqlTransformationKind.InsertToUpdate => InsertToUpdate(statement),
                SqlTransformationKind.ValuesToUnionAll => ValuesToUnionAll(statement),
                SqlTransformationKind.ExecWithReturnCode => ExecWithReturnCode(statement),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }
        catch (TransformationException exception)
        {
            return SqlTransformationResult.Failed(exception.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var replaced = source
            .Remove(statement.StartOffset, statement.FragmentLength)
            .Insert(statement.StartOffset, statementText);
        var parser = new ScriptDomSqlParser();
        if (!parser.Parse(replaced, SqlDialectVersion.Auto, cancellationToken).ParseSucceeded)
        {
            return SqlTransformationResult.Failed("The generated SQL could not be parsed; the transformation was not applied.");
        }

        var formatted = new ScriptDomSqlFormatter().Format(
            replaced, options, new FormatRequest(), cancellationToken);
        var document = formatted.ParseSucceeded &&
                       !formatted.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error) &&
                       !string.IsNullOrEmpty(formatted.Text)
            ? formatted.Text
            : replaced;
        if (!parser.Parse(document, SqlDialectVersion.Auto, cancellationToken).ParseSucceeded)
        {
            return SqlTransformationResult.Failed("The transformed document could not be parsed; the transformation was not applied.");
        }

        return SqlTransformationResult.Ok(
            document, statementText, new SqlTextSpan(statement.StartOffset, statement.FragmentLength));
    }

    private static string InsertToUpdate(TSqlStatement statement)
    {
        if (statement is not InsertStatement insert ||
            insert.InsertSpecification.InsertSource is not ValuesInsertSource values ||
            values.IsDefaultValues)
        {
            throw new TransformationException("The caret must be inside an INSERT ... VALUES statement.");
        }

        var specification = insert.InsertSpecification;
        if (specification.Columns.Count == 0)
        {
            throw new TransformationException("The INSERT requires an explicit column list.");
        }

        if (values.RowValues.Count != 1)
        {
            throw new TransformationException("The INSERT must contain exactly one VALUES row.");
        }

        var row = values.RowValues[0];
        if (row.ColumnValues.Count != specification.Columns.Count)
        {
            throw new TransformationException("The column list and the VALUES row have different lengths.");
        }

        var update = new UpdateStatement
        {
            UpdateSpecification = new UpdateSpecification
            {
                Target = specification.Target
            }
        };
        for (var index = 0; index < specification.Columns.Count; index++)
        {
            update.UpdateSpecification.SetClauses.Add(new AssignmentSetClause
            {
                Column = specification.Columns[index],
                NewValue = row.ColumnValues[index]
            });
        }

        update.UpdateSpecification.WhereClause = new WhereClause
        {
            SearchCondition = new BooleanComparisonExpression
            {
                FirstExpression = new IntegerLiteral { Value = "1" },
                ComparisonType = BooleanComparisonType.Equals,
                SecondExpression = new IntegerLiteral { Value = "1" }
            }
        };

        return Generate(update).TrimEnd() + " /* TODO: replace the WHERE condition */;";
    }

    private static string ValuesToUnionAll(TSqlStatement statement)
    {
        if (statement is not InsertStatement insert ||
            insert.InsertSpecification.InsertSource is not ValuesInsertSource values ||
            values.IsDefaultValues)
        {
            throw new TransformationException("The caret must be inside an INSERT ... VALUES statement.");
        }

        var specification = insert.InsertSpecification;
        if (specification.Columns.Count == 0)
        {
            throw new TransformationException("The INSERT requires an explicit column list to name the columns.");
        }

        if (values.RowValues.Count == 0)
        {
            throw new TransformationException("The INSERT contains no VALUES rows.");
        }

        foreach (var row in values.RowValues)
        {
            if (row.ColumnValues.Count != specification.Columns.Count)
            {
                throw new TransformationException(
                    "Every VALUES row must have the same length as the column list.");
            }
        }

        var queries = new List<QuerySpecification>();
        for (var rowIndex = 0; rowIndex < values.RowValues.Count; rowIndex++)
        {
            var row = values.RowValues[rowIndex];
            var query = new QuerySpecification();
            for (var index = 0; index < row.ColumnValues.Count; index++)
            {
                var element = new SelectScalarExpression { Expression = row.ColumnValues[index] };
                if (rowIndex == 0)
                {
                    element.ColumnName = new IdentifierOrValueExpression
                    {
                        Identifier = ColumnIdentifier(specification.Columns[index])
                    };
                }

                query.SelectElements.Add(element);
            }

            queries.Add(query);
        }

        QueryExpression folded = queries[0];
        for (var index = 1; index < queries.Count; index++)
        {
            folded = new BinaryQueryExpression
            {
                FirstQueryExpression = folded,
                SecondQueryExpression = queries[index],
                BinaryQueryExpressionType = BinaryQueryExpressionType.Union,
                All = true
            };
        }

        var result = new SelectStatement { QueryExpression = folded };
        return Generate(result).TrimEnd() + ";";
    }

    private static string ExecWithReturnCode(TSqlStatement statement)
    {
        if (statement is not ExecuteStatement execute)
        {
            throw new TransformationException("The caret must be inside an EXEC statement.");
        }

        if (execute.ExecuteSpecification.Variable is not null)
        {
            throw new TransformationException("The EXEC statement already captures a return code.");
        }

        execute.ExecuteSpecification.Variable =
            new VariableReference { Name = ReturnCodeVariable };
        // Replacing a single IF/ELSE/WHILE body with two statements must retain
        // its scope. Always emit one block, including at the top level.
        var block = new BeginEndBlockStatement { StatementList = new StatementList() };
        block.StatementList.Statements.Add(new DeclareVariableStatement
        {
            Declarations =
            {
                new DeclareVariableElement
                {
                    VariableName = new Identifier { Value = ReturnCodeVariable },
                    DataType = new SqlDataTypeReference { SqlDataTypeOption = SqlDataTypeOption.Int }
                }
            }
        });
        block.StatementList.Statements.Add(execute);
        return Generate(block).TrimEnd();
    }

    private static Identifier ColumnIdentifier(ColumnReferenceExpression column)
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null || identifiers.Count == 0)
        {
            throw new TransformationException("An INSERT column could not be resolved.");
        }

        var identifier = identifiers[identifiers.Count - 1];
        return new Identifier { Value = identifier.Value, QuoteType = identifier.QuoteType };
    }

    private static string Generate(TSqlFragment fragment)
    {
        var generator = new Sql150ScriptGenerator();
        generator.GenerateScript(fragment, out var script);
        return script;
    }

    private static TSqlStatement? FindStatementAt(SqlParseResult parseResult, int offset) =>
        new SqlFragmentWalker().Walk(parseResult.Root!)
            .OfType<TSqlStatement>()
            .Where(statement => statement.StartOffset <= offset &&
                offset < statement.StartOffset + statement.FragmentLength)
            .OrderBy(statement => statement.FragmentLength)
            .FirstOrDefault();

    private sealed class TransformationException : Exception
    {
        public TransformationException(string message) : base(message)
        {
        }
    }
}
