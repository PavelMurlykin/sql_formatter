using System.Text;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Generation;

/// <summary>
/// Generates SELECT/INSERT/UPDATE/DELETE templates and an upsert procedure stub from a
/// parsed table shape; the result is formatted with the given profile options.
/// Identity columns are excluded from INSERT, UPDATE and parameters.
/// </summary>
public static class CrudGenerator
{
    public static string Generate(
        SqlTableDefinition table,
        FormattingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var writable = table.Columns.Where(column => !column.IsIdentity).ToList();
        var parameters = ParameterNames(writable);
        var target = table.Schema.Length > 0
            ? SqlIdentifierText.Format(table.Schema) + "." + SqlIdentifierText.Format(table.Name)
            : SqlIdentifierText.Format(table.Name);
        var builder = new StringBuilder();

        builder.AppendLine($"-- SELECT for {table.DisplayName}");
        builder.AppendLine(
            $"SELECT {string.Join(", ", table.Columns.Select(column => SqlIdentifierText.Format(column.Name)))} FROM {target};");

        if (writable.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"-- INSERT for {table.DisplayName}");
            builder.AppendLine(
                $"INSERT INTO {target} ({string.Join(", ", writable.Select(c => SqlIdentifierText.Format(c.Name)))}) VALUES ({string.Join(", ", writable.Select(c => parameters[c]))});");
            builder.AppendLine();
            builder.AppendLine($"-- UPDATE for {table.DisplayName}");
            builder.AppendLine(
                $"UPDATE {target} SET {Assignments(writable, parameters)} WHERE 1 = 1; /* TODO: key */");
            builder.AppendLine();
            builder.AppendLine($"-- Upsert procedure stub for {table.DisplayName}");
            builder.AppendLine("GO");
            const string prefix = "usp_", suffix = "_Upsert";
            var procedureName = prefix + table.Name.Substring(0,
                Math.Min(table.Name.Length, SqlIdentifierText.MaxLength - prefix.Length - suffix.Length)) + suffix;
            builder.AppendLine($"CREATE OR ALTER PROCEDURE {SqlIdentifierText.Format(table.SchemaOrDbo)}.{SqlIdentifierText.Format(procedureName)}");
            builder.AppendLine(Parameters(writable, parameters));
            builder.AppendLine("AS");
            builder.AppendLine("BEGIN");
            builder.AppendLine("    SET NOCOUNT ON;");
            builder.AppendLine($"    UPDATE {target} SET {Assignments(writable, parameters)} WHERE 1 = 1; -- TODO: key");
            builder.AppendLine("END");
            builder.AppendLine("GO");
        }

        builder.AppendLine();
        builder.AppendLine($"-- DELETE for {table.DisplayName}");
        builder.AppendLine($"DELETE FROM {target} WHERE 1 = 1; /* TODO: key */");

        var raw = builder.ToString();
        cancellationToken.ThrowIfCancellationRequested();
        var parser = new ScriptDomSqlParser();
        if (!parser.Parse(raw, SqlDialectVersion.Auto, cancellationToken).ParseSucceeded)
        {
            throw new InvalidOperationException("The generated CRUD SQL could not be parsed; no SQL was produced.");
        }

        var formatted = new ScriptDomSqlFormatter().Format(raw, options, new FormatRequest(), cancellationToken);
        var output = formatted.ParseSucceeded &&
               !formatted.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error) &&
               !string.IsNullOrEmpty(formatted.Text)
            ? formatted.Text
            : raw;
        if (!parser.Parse(output, SqlDialectVersion.Auto, cancellationToken).ParseSucceeded)
        {
            throw new InvalidOperationException("The formatted CRUD SQL could not be parsed; no SQL was produced.");
        }

        return output;
    }

    private static string Assignments(IEnumerable<TableColumn> columns,
        IReadOnlyDictionary<TableColumn, string> parameters) => string.Join(", ",
        columns.Select(column => $"{SqlIdentifierText.Format(column.Name)} = {parameters[column]}"));

    private static string Parameters(IReadOnlyList<TableColumn> columns,
        IReadOnlyDictionary<TableColumn, string> parameters) => string.Join(", ",
        columns.Select(column => $"{parameters[column]} {column.DataType}{(column.IsNullable ? " = NULL" : string.Empty)}"));

    private static IReadOnlyDictionary<TableColumn, string> ParameterNames(IReadOnlyList<TableColumn> columns)
    {
        // Reserve all usable source names first so a fallback cannot steal a
        // later column's natural parameter name. Variables cannot be delimited.
        var reserved = new HashSet<string>(columns.Select(c => "@" + c.Name)
            .Where(SqlIdentifierText.IsVariableName), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<TableColumn, string>();
        for (var index = 0; index < columns.Count; index++)
        {
            var name = "@" + columns[index].Name;
            if (!SqlIdentifierText.IsVariableName(name) || !used.Add(name))
            {
                var stem = "@column" + (index + 1);
                name = stem;
                var suffix = 2;
                while (reserved.Contains(name) || !used.Add(name))
                {
                    name = stem + "_" + suffix++;
                }
            }

            result.Add(columns[index], name);
        }

        return result;
    }
}
