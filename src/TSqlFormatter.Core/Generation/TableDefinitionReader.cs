using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Generation;

/// <summary>
/// Reads the first CREATE TABLE statement — anywhere in the script or within a scope —
/// into a column model. Computed columns without a data type are skipped; columns
/// without an explicit NULL/NOT NULL constraint count as nullable (the T-SQL default).
/// </summary>
public sealed class TableDefinitionReader
{
    public SqlTableDefinition? Read(SqlParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        return Read(parseResult, new SqlTextSpan(0, parseResult.Source.Length));
    }

    public SqlTableDefinition? Read(SqlParseResult parseResult, SqlTextSpan scope)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        if (parseResult.Root is null)
        {
            return null;
        }

        var statement = new SqlFragmentWalker().Walk(parseResult.Root)
            .OfType<CreateTableStatement>()
            .Where(candidate => candidate.StartOffset >= scope.StartOffset &&
                candidate.StartOffset + candidate.FragmentLength <= scope.EndOffset)
            .OrderBy(candidate => candidate.StartOffset)
            .FirstOrDefault();
        if (statement?.SchemaObjectName is null || statement.Definition is null)
        {
            return null;
        }

        var identifiers = statement.SchemaObjectName.Identifiers;
        if (identifiers.Count == 0)
        {
            return null;
        }

        var tableName = identifiers[identifiers.Count - 1].Value;
        var schema = identifiers.Count > 1 ? identifiers[identifiers.Count - 2].Value : string.Empty;
        var columns = new List<TableColumn>();
        foreach (var definition in statement.Definition.ColumnDefinitions)
        {
            var column = ToColumn(definition);
            if (column is not null)
            {
                columns.Add(column);
            }
        }

        if (columns.Count == 0)
        {
            return null;
        }

        return new SqlTableDefinition(
            schema, tableName, columns,
            new SqlTextSpan(statement.StartOffset, statement.FragmentLength));
    }

    private static TableColumn? ToColumn(ColumnDefinition definition)
    {
        if (definition.ColumnIdentifier is null || definition.DataType is null ||
            definition.ComputedColumnExpression is not null)
        {
            return null;
        }

        var nullableConstraint = definition.Constraints
            .OfType<NullableConstraintDefinition>()
            .FirstOrDefault();
        return new TableColumn(
            definition.ColumnIdentifier.Value,
            TypeText(definition.DataType),
            nullableConstraint?.Nullable ?? true,
            definition.IdentityOptions is not null);
    }

    private static string TypeText(DataTypeReference dataType)
    {
        if (dataType is SqlDataTypeReference sqlType)
        {
            var text = sqlType.SqlDataTypeOption.ToString().ToUpperInvariant();
            if (sqlType.Parameters.Count > 0)
            {
                text += "(" + ParametersText(sqlType) + ")";
            }

            return text;
        }

        if (dataType is XmlDataTypeReference)
        {
            return "XML";
        }

        if (dataType is ParameterizedDataTypeReference named && named.Name is not null)
        {
            var text = string.Join(".", named.Name.Identifiers.Select(identifier => identifier.Value));
            if (named.Parameters.Count > 0)
            {
                text += "(" + ParametersText(named) + ")";
            }

            return text;
        }

        return "?";
    }

    private static string ParametersText(ParameterizedDataTypeReference dataType) => string.Join(", ",
        dataType.Parameters
            .Select(parameter => parameter.Value)
            .Select(value => value.Equals("max", StringComparison.OrdinalIgnoreCase) ? "MAX" : value));
}
