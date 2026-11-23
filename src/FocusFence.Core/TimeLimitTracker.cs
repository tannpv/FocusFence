namespace FocusFence.Core;

public sealed record TimeLimitEvaluation(IReadOnlyList<AppTarget> Blocked, IReadOnlyList<AppTarget> Warnings);

public sealed class TimeLimitTracker(AppState state, TimeProvider clock, IForegroundTracker foreground)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public void RecordActiveTime(int elapsedSeconds = 1)
    {
        var active = foreground.GetActiveExecutablePath();
        if (string.IsNullOrWhiteSpace(active)) return;
        var limit = state.TimeLimits.FirstOrDefault(l => l.Enabled && string.Equals(l.Path, active, StringComparison.OrdinalIgnoreCase));
        if (limit is null) return;
        var usage = GetOrCreateTodayUsage(limit.Path);
        usage.UsedSeconds += elapsedSeconds;
    }

    public TimeLimitEvaluation Evaluate()
    {
        var blocked = new List<AppTarget>();
        var warnings = new List<AppTarget>();
        foreach (var limit in state.TimeLimits.Where(l => l.Enabled))
        {
            var usage = GetOrCreateTodayUsage(limit.Path);
            var totalAllowed = (limit.DailyMinutes + usage.BonusMinutes) * 60;
            var remaining = Math.Max(0, totalAllowed - usage.UsedSeconds);
            if (remaining == 0)
            {
                blocked.Add(new(limit.Name, limit.Path));
            }
            else if (remaining <= 300 && !usage.WarningEmitted)
            {
                warnings.Add(new(limit.Name, limit.Path));
                usage.WarningEmitted = true;
            }
        }
        return new(blocked, warnings);
    }

    public void GrantBonusMinutes(string path, int minutes)
    {
        var usage = GetOrCreateTodayUsage(path);
        usage.BonusMinutes += minutes;
        var limit = state.TimeLimits.FirstOrDefault(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase));
        if (limit is not null && ((limit.DailyMinutes + usage.BonusMinutes) * 60 - usage.UsedSeconds) > 300)
            usage.WarningEmitted = false;
    }

    public TimeLimitStatus? GetStatus(string path)
    {
        var limit = state.TimeLimits.FirstOrDefault(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase));
        if (limit is null) return null;
        var usage = state.DailyUsages.FirstOrDefault(u => u.Date == Today && string.Equals(u.Path, path, StringComparison.OrdinalIgnoreCase));
        var used = usage?.UsedSeconds ?? 0;
        var bonus = usage?.BonusMinutes ?? 0;
        var total = (limit.DailyMinutes + bonus) * 60;
        var remaining = Math.Max(0, total - used);
        return new(limit, used, total, remaining, remaining == 0);
    }

    public void PruneOldUsages(int retentionDays = 14)
    {
        var cutoff = Today.AddDays(-retentionDays);
        state.DailyUsages.RemoveAll(u => u.Date < cutoff);
    }

    private AppDailyUsage GetOrCreateTodayUsage(string path)
    {
        var usage = state.DailyUsages.FirstOrDefault(u => u.Date == Today && string.Equals(u.Path, path, StringComparison.OrdinalIgnoreCase));
        if (usage is null)
        {
            usage = new() { Date = Today, Path = path };
            state.DailyUsages.Add(usage);
        }
        return usage;
    }
}
