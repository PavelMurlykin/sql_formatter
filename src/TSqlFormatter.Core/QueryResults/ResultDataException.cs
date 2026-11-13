namespace TSqlFormatter.Core.QueryResults;

public enum ResultDataError
{
    EmptyInput, TooLarge, TooManyRows, TooManyColumns, TooManyCells, MalformedTsv,
    UnequalColumns, NoDataRows, InRequiresOneColumn, InvalidHeaders, InvalidTable,
    FormatFailed, NumericOverflow
}

/// <summary>A rejected clipboard conversion; no partial SQL or statistics are returned.</summary>
public sealed class ResultDataException : ArgumentException
{
    public ResultDataException(ResultDataError code) : base(code switch
    {
        ResultDataError.EmptyInput => "The clipboard contains no TSV text.",
        ResultDataError.TooLarge => "TSV input exceeds 1 Mi characters.",
        ResultDataError.TooManyRows => "TSV input exceeds 10000 rows, including headers.",
        ResultDataError.TooManyColumns => "TSV input exceeds 256 columns.",
        ResultDataError.TooManyCells => "TSV input exceeds 100000 cells.",
        ResultDataError.MalformedTsv => "Invalid TSV quotes or a NUL character.",
        ResultDataError.UnequalColumns => "Every TSV row must have the same number of columns.",
        ResultDataError.NoDataRows => "No data rows remain after the header.",
        ResultDataError.InRequiresOneColumn => "IN requires exactly one TSV column.",
        ResultDataError.InvalidHeaders => "INSERT headers must be nonempty, unique names of at most 128 characters.",
        ResultDataError.InvalidTable => "Specify one to three table-name parts, optionally delimited with brackets.",
        ResultDataError.NumericOverflow => "A numeric cell or the sum cannot be represented exactly as a decimal.",
        _ => "The generated SQL could not be parsed or formatted."
    }) { Code = code; }
    public ResultDataError Code { get; }
}
