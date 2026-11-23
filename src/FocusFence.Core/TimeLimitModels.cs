namespace FocusFence.Core;

public sealed record AppTimeLimit(string Path, string Name, int DailyMinutes, bool Enabled = true)
{
    public static string? Validate(AppTimeLimit limit)
    {
        if (string.IsNullOrWhiteSpace(limit.Path)) return "Executable path is required.";
        if (string.IsNullOrWhiteSpace(limit.Name)) return "Application name is required.";
        if (limit.DailyMinutes is < 1 or > Defaults.MaximumSessionMinutes)
            return $"Daily limit must be between 1 and {Defaults.MaximumSessionMinutes} minutes.";
        return null;
    }
}

public sealed class AppDailyUsage
{
    public DateOnly Date { get; set; }
    public string Path { get; set; } = "";
    public int UsedSeconds { get; set; }
    public int BonusMinutes { get; set; }
    public bool WarningEmitted { get; set; }
    public bool ExhaustedEmitted { get; set; }
}

public sealed record TimeLimitStatus(AppTimeLimit Limit, int UsedSeconds, int TotalAllowedSeconds, int RemainingSeconds, bool IsExhausted);
