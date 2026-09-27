using System.Text;
using System.Text.RegularExpressions;

namespace TSqlFormatter.Configuration;

/// <summary>Matches semicolon-separated file-name and path globs for save automation.</summary>
public sealed class SqlSaveExclusionMatcher
{
    public bool IsExcluded(string path, string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns)) return false;
        if (path is null) throw new ArgumentNullException(nameof(path));

        string normalized = Path.GetFullPath(path).Replace('\\', '/');
        string fileName = Path.GetFileName(normalized) ?? string.Empty;
        foreach (string raw in patterns!.Split(';'))
        {
            string pattern = raw.Trim().Replace('\\', '/');
            if (pattern.Length == 0) continue;
            if (pattern.Length > 4096)
                throw new ArgumentException("A save exclusion pattern exceeds 4096 characters.", nameof(patterns));

            bool pathPattern = pattern.Contains('/');
            string input = pathPattern ? normalized : fileName;
            string expression = (pathPattern ? "(?:^|/)" : "^") + Translate(pattern) + "$";
            if (Regex.IsMatch(input, expression,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)))
                return true;
        }

        return false;
    }

    private static string Translate(string pattern)
    {
        var regex = new StringBuilder();
        for (int index = 0; index < pattern.Length; index++)
        {
            switch (pattern[index])
            {
                case '*' when index + 1 < pattern.Length && pattern[index + 1] == '*':
                    regex.Append(".*");
                    index++;
                    break;
                case '*':
                    regex.Append("[^/]*");
                    break;
                case '?':
                    regex.Append("[^/]");
                    break;
                default:
                    regex.Append(Regex.Escape(pattern[index].ToString()));
                    break;
            }
        }

        return regex.ToString();
    }
}
