using System.Globalization;
using System.Numerics;

namespace TSqlFormatter.Core.QueryResults;

public sealed class TsvStatistics
{
    private TsvStatistics(int cells, int numeric, decimal sum)
    { TotalCells = cells; NumericCount = numeric; Sum = sum; Average = numeric == 0 ? null : sum / numeric; }
    public int TotalCells { get; }
    public int NumericCount { get; }
    public int SkippedCount => TotalCells - NumericCount;
    public decimal Sum { get; }
    public decimal? Average { get; }

    public static TsvStatistics Calculate(TsvTable table, bool firstRowHeaders = false, CancellationToken cancellationToken = default)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        cancellationToken.ThrowIfCancellationRequested();
        int start = table.DataStart(firstRowHeaders), count = 0;
        BigInteger exactSum = BigInteger.Zero;
        int scale = 0;
        for (int row = start; row < table.Rows.Count; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var cell in table.Rows[row])
            {
                if (!TsvNumeric.TryNormalize(cell.Text.Trim(), false, out string number)) continue;
                if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal value) || TsvNumeric.Display(value) != number)
                    throw new ResultDataException(ResultDataError.NumericOverflow);
                int dot = number.IndexOf('.'), cellScale = dot < 0 ? 0 : number.Length - dot - 1;
                if (cellScale > scale) { exactSum *= BigInteger.Pow(10, cellScale - scale); scale = cellScale; }
                exactSum += BigInteger.Parse(number.Replace(".", ""), CultureInfo.InvariantCulture) * BigInteger.Pow(10, scale - cellScale);
                count++;
            }
        }
        string digits = BigInteger.Abs(exactSum).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        string sumText = (exactSum.Sign < 0 ? "-" : "") + (scale == 0 ? digits : digits.Insert(digits.Length - scale, "."));
        TsvNumeric.TryNormalize(sumText, false, out string canonical);
        if (!decimal.TryParse(canonical, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal sum) || TsvNumeric.Display(sum) != canonical)
            throw new ResultDataException(ResultDataError.NumericOverflow);
        return new((table.Rows.Count - start) * table.ColumnCount, count, sum);
    }
}
