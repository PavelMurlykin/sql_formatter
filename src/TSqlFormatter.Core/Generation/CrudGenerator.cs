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
        var builder = new StringBuilder();

        builder.AppendLine($"-- SELECT for {table.DisplayName}");
        builder.AppendLine(
            $"SELECT {string.Join(", ", table.Columns.Select(column => column.Name))} FROM {table.DisplayName};");

        if (writable.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"-- INSERT for {table.DisplayName}");
            builder.AppendLine(
                $"INSERT INTO {table.DisplayName} ({string.Join(", ", writable.Select(c => c.Name))}) VALUES ({string.Join(", ", writable.Select(c => "@" + c.Name))});");
            builder.AppendLine();
            builder.AppendLine($"-- UPDATE for {table.DisplayName}");
            builder.AppendLine(
                $"UPDATE {table.DisplayName} SET {Assignments(writable)} WHERE 1 = 1; /* TODO: key */");
            builder.AppendLine();
            builder.AppendLine($"-- Upsert procedure stub for {table.DisplayName}");
            builder.AppendLine("GO");
            builder.AppendLine($"CREATE OR ALTER PROCEDURE {table.SchemaOrDbo}.usp_{table.Name}_Upsert");
            builder.AppendLine(Parameters(writable));
            builder.AppendLine("AS");
            builder.AppendLine("BEGIN");
            builder.AppendLine("    SET NOCOUNT ON;");
            builder.AppendLine($"    UPDATE {table.DisplayName} SET {Assignments(writable)} WHERE 1 = 1; -- TODO: key");
            builder.AppendLine("END");
            builder.AppendLine("GO");
        }

        builder.AppendLine();
        builder.AppendLine($"-- DELETE for {table.DisplayName}");
        builder.AppendLine($"DELETE FROM {table.DisplayName} WHERE 1 = 1; /* TODO: key */");

        var raw = builder.ToString();
        cancellationToken.ThrowIfCancellationRequested();
        var formatted = new ScriptDomSqlFormatter().Format(raw, options, new FormatRequest(), cancellationToken);
        return formatted.ParseSucceeded &&
               !formatted.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error) &&
               !string.IsNullOrEmpty(formatted.Text)
            ? formatted.Text
            : raw;
    }

    private static string Assignments(IEnumerable<TableColumn> columns) => string.Join(", ",
        columns.Select(column => $"{column.Name} = @{column.Name}"));

    private static string Parameters(IReadOnlyList<TableColumn> columns) => string.Join(", ",
        columns.Select(column => $"@{column.Name} {column.DataType}{(column.IsNullable ? " = NULL" : string.Empty)}"));
}
