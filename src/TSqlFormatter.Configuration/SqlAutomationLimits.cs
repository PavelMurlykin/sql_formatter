namespace TSqlFormatter.Configuration;

/// <summary>Conservative limits for experimental editor automation.</summary>
public static class SqlAutomationLimits
{
    public const int MaxAutomaticCharacters = 8 * 1024;

    public static bool CanProcess(int characterCount) =>
        characterCount >= 0 && characterCount <= MaxAutomaticCharacters;
}
