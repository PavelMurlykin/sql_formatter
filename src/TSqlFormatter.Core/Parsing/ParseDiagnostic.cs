namespace TSqlFormatter.Core.Parsing;

public sealed class ParseDiagnostic
{
    public ParseDiagnostic(int parserErrorNumber, string message, int offset, int line, int column)
    {
        ParserErrorNumber = parserErrorNumber;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Offset = offset;
        Line = line;
        Column = column;
    }

    public int ParserErrorNumber { get; }

    public string Message { get; }

    public int Offset { get; }

    public int Line { get; }

    public int Column { get; }
}
