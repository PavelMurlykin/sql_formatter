using System.Globalization;

namespace TSqlFormatter.Core.QueryResults;

internal static class TsvNumeric
{
    // No locale guessing, grouping or exponents. Canonicalization never uses floating point.
    internal static bool TryNormalize(string text, bool preserveLeadingZeros, out string number)
    {
        number = "";
        int start = text.Length > 0 && text[0] is '+' or '-' ? 1 : 0;
        if (start == text.Length) return false;
        int dot = text.IndexOf('.', start), end = dot < 0 ? text.Length : dot;
        if (end == start || dot == text.Length - 1) return false;
        for (int i = start; i < text.Length; i++)
            if (i != dot && (text[i] < '0' || text[i] > '9')) return false;
        if (preserveLeadingZeros && end - start > 1 && text[start] == '0') return false;
        string integer = text.Substring(start, end - start).TrimStart('0');
        if (integer.Length == 0) integer = "0";
        string fraction = dot < 0 ? "" : text.Substring(dot + 1).TrimEnd('0');
        number = (start == 1 && text[0] == '-' && (integer != "0" || fraction.Length > 0) ? "-" : "") +
            integer + (fraction.Length > 0 ? "." + fraction : "");
        return true;
    }
    internal static string Display(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
