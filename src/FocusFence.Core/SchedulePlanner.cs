namespace FocusFence.Core;

/// <summary>Evaluates calendar windows without timers, UI dependencies, or process access.</summary>
public sealed class SchedulePlanner(AppState state, TimeProvider clock)
{
    public IReadOnlyList<ScheduleOccurrence> ActiveOccurrences()
    {
        var now = clock.GetUtcNow();
        var result = new List<ScheduleOccurrence>();
        foreach (var schedule in state.Schedules.Where(s => s.Enabled))
        {
            var apps = state.Blocklists.FirstOrDefault(b => b.Id == schedule.BlocklistId)?.Apps;
            if (apps is not { Count: > 0 }) continue;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            // Yesterday matters when a window crosses midnight.
            foreach (var date in new[] { today.AddDays(-1), today })
            {
                if (!schedule.Days.Contains(date.DayOfWeek)) continue;
                var start = Resolve(date.ToDateTime(schedule.StartsAt), zone, isEnd: false);
                var endDate = schedule.EndsAt < schedule.StartsAt ? date.AddDays(1) : date;
                var end = Resolve(endDate.ToDateTime(schedule.EndsAt), zone, isEnd: true);
                if (now < start || now >= end) continue;
                if (state.SkippedSchedules.TryGetValue(schedule.Id, out var skippedUntil) && skippedUntil >= end) continue;
                result.Add(new(schedule.Id, end, apps));
            }
        }
        return result;
    }

    public void SkipCurrentOccurrences()
    {
        foreach (var occurrence in ActiveOccurrences())
            state.SkippedSchedules[occurrence.ScheduleId] = occurrence.EndsAt;
    }

    public static string? Validate(FocusSchedule schedule, IEnumerable<Blocklist> blocklists)
    {
        if (string.IsNullOrWhiteSpace(schedule.Name)) return "Give the schedule a name.";
        if (!blocklists.Any(b => b.Id == schedule.BlocklistId)) return "Choose an existing blocklist.";
        if (schedule.Days is not { Count: > 0 } || schedule.Days.Any(d => !Enum.IsDefined(d))) return "Choose at least one valid day.";
        if (schedule.StartsAt == schedule.EndsAt) return "Start and end times must be different.";
        if (string.IsNullOrWhiteSpace(schedule.TimeZoneId)) return "Choose a valid time zone.";
        try { TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return "The schedule time zone is unavailable."; }
        return null;
    }

    private static DateTimeOffset Resolve(DateTime localTime, TimeZoneInfo zone, bool isEnd)
    {
        // Spring-forward gaps move to the first valid minute. Fall-back windows
        // include both copies of an ambiguous time: earliest start, latest end.
        while (zone.IsInvalidTime(localTime)) localTime = localTime.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(localTime)
            ? (isEnd ? zone.GetAmbiguousTimeOffsets(localTime).Min() : zone.GetAmbiguousTimeOffsets(localTime).Max())
            : zone.GetUtcOffset(localTime);
        return new DateTimeOffset(localTime, offset).ToUniversalTime();
    }
}
