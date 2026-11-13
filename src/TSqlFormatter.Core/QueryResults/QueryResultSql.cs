using System.Globalization;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.QueryResults;

public sealed class QueryResultOptions
{
    public QueryResultOptions(bool firstRowHeaders = false, bool allValuesAsText = false)
    { FirstRowHeaders = firstRowHeaders; AllValuesAsText = allValuesAsText; }
    public bool FirstRowHeaders { get; }
    public bool AllValuesAsText { get; }
}

public static class QueryResultSql
{
    public static string CreateIn(TsvTable table, QueryResultOptions options, FormattingOptions formatting,
        CancellationToken cancellationToken = default)
    {
        Validate(table, options, formatting, cancellationToken);
        if (table.ColumnCount != 1) throw new ResultDataException(ResultDataError.InRequiresOneColumn);
        var values = Literals(table, options, cancellationToken);
        string sql = Format("SELECT 1 WHERE [__tsv_value__] IN (" + string.Join(", ", values.Select(r => r[0])) + ");", formatting, cancellationToken);
        var parsed = new ScriptDomSqlParser().Parse(sql, SqlDialectVersion.Auto, cancellationToken);
        var visitor = new InVisitor(); parsed.Root!.Accept(visitor);
        var predicate = visitor.Predicate ?? throw new ResultDataException(ResultDataError.FormatFailed);
        var tokens = parsed.Root.ScriptTokenStream;
        var first = tokens.Skip(predicate.FirstTokenIndex).Take(predicate.LastTokenIndex - predicate.FirstTokenIndex + 1)
            .First(t => t.TokenType == TSqlTokenType.In);
        var last = tokens[predicate.LastTokenIndex];
        return sql.Substring(first.Offset, last.Offset + last.Text.Length - first.Offset);
    }

    public static string CreateInsert(TsvTable table, string target, QueryResultOptions options, FormattingOptions formatting,
        CancellationToken cancellationToken = default)
    {
        Validate(table, options, formatting, cancellationToken);
        string name = NormalizeTableName(target);
        string columns = "";
        if (options.FirstRowHeaders)
        {
            var headers = table.Rows[0].Select(c => c.Text).ToArray();
            if (headers.Any(h => string.IsNullOrWhiteSpace(h) || h.Length > 128 || h.Any(char.IsControl)) ||
                headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
                throw new ResultDataException(ResultDataError.InvalidHeaders);
            columns = " (" + string.Join(", ", headers.Select(SqlIdentifierText.Format)) + ")";
        }
        var values = Literals(table, options, cancellationToken);
        var sql = new StringBuilder();
        // SQL Server limits INSERT ... VALUES to 1000 rows per statement.
        for (int start = 0; start < values.Count; start += 1000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sql.Append("INSERT INTO ").Append(name).Append(columns).AppendLine(" VALUES");
            sql.Append(string.Join(",\n", values.Skip(start).Take(1000).Select(r => "(" + string.Join(", ", r) + ")")));
            sql.AppendLine(";");
        }
        return Format(sql.ToString(), formatting, cancellationToken);
    }

    /// <summary>Identifiers only, never arbitrary SQL. Always emits delimited components.</summary>
    public static string NormalizeTableName(string target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (target.Length > 512) throw new ResultDataException(ResultDataError.InvalidTable);
        var parts = new List<string>(); int i = 0;
        while (i < target.Length)
        {
            while (i < target.Length && char.IsWhiteSpace(target[i])) i++;
            if (i == target.Length) throw new ResultDataException(ResultDataError.InvalidTable);
            string name;
            if (target[i] == '[')
            {
                var decoded = new StringBuilder(); bool closed = false; i++;
                while (i < target.Length)
                {
                    char ch = target[i++];
                    if (ch != ']') decoded.Append(ch);
                    else if (i < target.Length && target[i] == ']') { decoded.Append(']'); i++; }
                    else { closed = true; break; }
                }
                if (!closed) throw new ResultDataException(ResultDataError.InvalidTable);
                name = decoded.ToString();
            }
            else
            {
                int start = i; while (i < target.Length && target[i] != '.') i++;
                name = target.Substring(start, i - start).Trim();
                if (!SqlIdentifierText.IsPlainIdentifier(name)) throw new ResultDataException(ResultDataError.InvalidTable);
            }
            if (name.Length == 0 || name.Length > 128 || name.Any(char.IsControl)) throw new ResultDataException(ResultDataError.InvalidTable);
            parts.Add("[" + name.Replace("]", "]]") + "]");
            if (parts.Count > 3) throw new ResultDataException(ResultDataError.InvalidTable);
            while (i < target.Length && char.IsWhiteSpace(target[i])) i++;
            if (i == target.Length) break;
            if (target[i++] != '.' || i == target.Length) throw new ResultDataException(ResultDataError.InvalidTable);
        }
        if (parts.Count == 0) throw new ResultDataException(ResultDataError.InvalidTable);
        return string.Join(".", parts);
    }

    private enum Kind { Null, Number, Date, DateTime, Text }
    private static Kind Classify(TsvCell cell, bool allText)
    {
        if (allText || cell.WasQuoted) return Kind.Text;
        if (cell.Text.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return Kind.Null;
        if (TsvNumeric.TryNormalize(cell.Text, true, out string number) && number.Count(char.IsDigit) <= 38) return Kind.Number;
        if (DateTime.TryParseExact(cell.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return Kind.Date;
        if ((cell.Text.Length == 19 || cell.Text.Length is >= 21 and <= 27 && cell.Text[19] == '.') &&
            DateTime.TryParseExact(cell.Text, new[] { "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss.FFFFFFF" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return Kind.DateTime;
        return Kind.Text;
    }
    private static IReadOnlyList<string[]> Literals(TsvTable table, QueryResultOptions options, CancellationToken token)
    {
        int start = table.DataStart(options.FirstRowHeaders);
        var families = new Kind[table.ColumnCount];
        var integerDigits = new int[table.ColumnCount]; var scales = new int[table.ColumnCount];
        for (int col = 0; col < table.ColumnCount; col++)
        {
            Kind family = Kind.Null;
            for (int row = start; row < table.Rows.Count; row++)
            {
                token.ThrowIfCancellationRequested();
                var cell = table.Rows[row][col]; Kind kind = Classify(cell, options.AllValuesAsText);
                if (kind == Kind.Null) continue;
                if (kind == Kind.DateTime) kind = Kind.Date;
                if (family == Kind.Null) family = kind;
                else if (family != kind) family = Kind.Text;
                if (kind == Kind.Number)
                {
                    TsvNumeric.TryNormalize(cell.Text, true, out string number);
                    int dot = number.IndexOf('.'), sign = number[0] == '-' ? 1 : 0;
                    integerDigits[col] = Math.Max(integerDigits[col], (dot < 0 ? number.Length : dot) - sign);
                    scales[col] = Math.Max(scales[col], dot < 0 ? 0 : number.Length - dot - 1);
                }
            }
            families[col] = integerDigits[col] + scales[col] > 38 ? Kind.Text : family;
        }
        var result = new List<string[]>();
        for (int row = start; row < table.Rows.Count; row++)
        {
            token.ThrowIfCancellationRequested(); var values = new string[table.ColumnCount];
            for (int col = 0; col < table.ColumnCount; col++)
            {
                var cell = table.Rows[row][col]; Kind kind = Classify(cell, options.AllValuesAsText);
                values[col] = kind == Kind.Null ? "NULL" : families[col] switch
                {
                    Kind.Number => TsvNumeric.TryNormalize(cell.Text, true, out string number) ? number : throw new InvalidOperationException(),
                    Kind.Date => "CONVERT(" + (kind == Kind.Date ? "date" : "datetime2(7)") + ", " + Quote(cell.Text.Replace(' ', 'T')) + ", " + (kind == Kind.Date ? "23" : "126") + ")",
                    _ => Quote(cell.Text)
                };
            }
            result.Add(values);
        }
        return result;
    }
    private static string Quote(string value) => "N'" + value.Replace("'", "''") + "'";
    private static void Validate(TsvTable table, QueryResultOptions options, FormattingOptions formatting, CancellationToken token)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (formatting == null) throw new ArgumentNullException(nameof(formatting));
        token.ThrowIfCancellationRequested();
        table.DataStart(options.FirstRowHeaders);
    }
    private static string Format(string sql, FormattingOptions formatting, CancellationToken token)
    {
        var result = new ScriptDomSqlFormatter().Format(sql, formatting, new FormatRequest(), token);
        if (!result.ParseSucceeded || result.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error) ||
            !new ScriptDomSqlParser().Parse(result.Text, SqlDialectVersion.Auto, token).ParseSucceeded)
            throw new ResultDataException(ResultDataError.FormatFailed);
        return result.Text;
    }
    private sealed class InVisitor : TSqlFragmentVisitor
    {
        internal InPredicate? Predicate { get; private set; }
        public override void Visit(TSqlFragment node) { if (node is InPredicate predicate) Predicate = predicate; }
    }
}
