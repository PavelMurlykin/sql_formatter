using System.Text;

namespace TSqlFormatter.Core.QueryResults;

public sealed class TsvCell
{
    internal TsvCell(string text, bool quoted) { Text = text; WasQuoted = quoted; }
    public string Text { get; }
    public bool WasQuoted { get; }
}

/// <summary>Bounded, immutable TSV. Headers are always an explicit caller choice.</summary>
public sealed class TsvTable
{
    public const int MaxCharacters = 1024 * 1024, MaxRows = 10000, MaxColumns = 256, MaxCells = 100000;
    private TsvTable(IReadOnlyList<IReadOnlyList<TsvCell>> rows) { Rows = rows; }
    public IReadOnlyList<IReadOnlyList<TsvCell>> Rows { get; }
    public int ColumnCount => Rows[0].Count;
    internal int DataStart(bool firstRowHeaders)
    {
        int start = firstRowHeaders ? 1 : 0;
        if (Rows.Count <= start) throw new ResultDataException(ResultDataError.NoDataRows);
        return start;
    }

    public static TsvTable Parse(string source, CancellationToken cancellationToken = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Length == 0) throw new ResultDataException(ResultDataError.EmptyInput);
        if (source.Length > MaxCharacters) throw new ResultDataException(ResultDataError.TooLarge);
        var rows = new List<IReadOnlyList<TsvCell>>();
        var cells = new List<TsvCell>();
        var field = new StringBuilder();
        bool inQuotes = false, afterQuote = false, quoted = false, hasRecord = false;
        int cellCount = 0;
        void FinishField()
        {
            cells.Add(new(field.ToString(), quoted)); field.Clear(); quoted = afterQuote = false;
            if (cells.Count > MaxColumns) throw new ResultDataException(ResultDataError.TooManyColumns);
            if (++cellCount > MaxCells) throw new ResultDataException(ResultDataError.TooManyCells);
        }
        void FinishRow()
        {
            FinishField();
            if (rows.Count > 0 && rows[0].Count != cells.Count) throw new ResultDataException(ResultDataError.UnequalColumns);
            rows.Add(Array.AsReadOnly(cells.ToArray())); cells.Clear(); hasRecord = false;
            if (rows.Count > MaxRows) throw new ResultDataException(ResultDataError.TooManyRows);
        }
        for (int i = 0; i < source.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            char ch = source[i];
            if (ch == '\0') throw new ResultDataException(ResultDataError.MalformedTsv);
            if (inQuotes)
            {
                if (ch != '"') field.Append(ch);
                else if (i + 1 < source.Length && source[i + 1] == '"') { field.Append('"'); i++; }
                else { inQuotes = false; afterQuote = true; }
                continue;
            }
            if (afterQuote && ch is not ('\t' or '\r' or '\n')) throw new ResultDataException(ResultDataError.MalformedTsv);
            if (ch == '\t') { FinishField(); hasRecord = true; }
            else if (ch is '\r' or '\n')
            {
                FinishRow();
                if (ch == '\r' && i + 1 < source.Length && source[i + 1] == '\n') i++;
            }
            else if (ch == '"' && field.Length == 0) { inQuotes = quoted = hasRecord = true; }
            else { field.Append(ch); hasRecord = true; }
        }
        if (inQuotes) throw new ResultDataException(ResultDataError.MalformedTsv);
        if (hasRecord) FinishRow(); // One final record separator does not add an empty row.
        return new(Array.AsReadOnly(rows.ToArray()));
    }
}
