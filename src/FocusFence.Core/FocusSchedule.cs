namespace FocusFence.Core;

/// <summary>Days refer to the day the window starts, including overnight windows.</summary>
public sealed record FocusSchedule(
    Guid Id,
    string Name,
    Guid BlocklistId,
    List<DayOfWeek> Days,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    string TimeZoneId,
    bool Enabled);

public sealed record ScheduleOccurrence(Guid ScheduleId, DateTimeOffset EndsAt, IReadOnlyList<AppTarget> Apps);
