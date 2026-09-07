using FocusFence.Core;

internal static class ScheduleChecks
{
    public static void Run(Action<bool, string> check)
    {
        var clock = new CalendarClock(new(2026, 9, 7, 8, 59, 0, TimeSpan.Zero)); // Monday
        var first = new Blocklist(Guid.NewGuid(), "Work", [new("One", @"C:\Apps\one.exe")]);
        var second = new Blocklist(Guid.NewGuid(), "Study", [new("Two", @"C:\Apps\two.exe")]);
        var morning = new FocusSchedule(Guid.NewGuid(), "Morning", first.Id, [DayOfWeek.Monday], new(9, 0), new(11, 0), TimeZoneInfo.Utc.Id, true);
        var state = new AppState { Blocklists = [first, second], Schedules = [morning] };
        var planner = new SchedulePlanner(state, clock);
        var session = new SessionController(state, clock);
        check(session.Session is null, "Schedule inactive before its start");
        clock.Set(new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero));
        check(session.Session?.Apps.Count == 1, "Schedule starts exactly at configured time");
        check(session.Remaining == TimeSpan.FromHours(2), "Scheduled deadline is calculated without a timer");
        state.Schedules.Add(new(Guid.NewGuid(), "Overlap", second.Id, [DayOfWeek.Monday], new(10, 0), new(12, 0), TimeZoneInfo.Utc.Id, true));
        clock.Set(new(2026, 9, 7, 10, 30, 0, TimeSpan.Zero));
        check(session.Session?.Apps.Count == 2 && session.Remaining == TimeSpan.FromMinutes(30), "Overlaps combine apps until the next ending boundary");
        clock.Set(new(2026, 9, 7, 11, 0, 0, TimeSpan.Zero));
        check(session.Session?.Apps.Single().Path == second.Apps[0].Path, "Ended schedule releases only its apps");
        session.RequestUnlock(15);
        clock.Set(new(2026, 9, 7, 11, 0, 15, TimeSpan.Zero));
        session.CompleteUnlock();
        check(session.Session is null && new SessionController(state, clock).Session is null, "Unlocked schedule stays skipped across controller restart");
        clock.Set(new(2026, 9, 14, 10, 30, 0, TimeSpan.Zero));
        check(session.Session?.Apps.Count == 2, "Skipping a period does not disable future weeks");
        session.RequestUnlock(1);
        clock.Set(new(2026, 9, 14, 10, 30, 1, TimeSpan.Zero));
        session.CompleteUnlock();
        check(session.Session is null && state.SkippedSchedules.Count == 2, "Emergency unlock skips every currently overlapping period");

        state.Schedules = [morning with { StartsAt = new(22, 0), EndsAt = new(2, 0) }];
        state.SkippedSchedules.Clear();
        clock.Set(new(2026, 9, 8, 1, 0, 0, TimeSpan.Zero));
        check(planner.ActiveOccurrences().Count == 1, "Monday overnight schedule remains active early Tuesday");
        clock.Set(new(2026, 9, 8, 2, 0, 0, TimeSpan.Zero));
        check(planner.ActiveOccurrences().Count == 0, "Overnight schedule ends at the exact boundary");
        clock.Set(new(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        check(planner.ActiveOccurrences().Count == 0, "Unselected start days do not activate overnight blocking");
        state.Schedules = [morning with { Enabled = false }];
        clock.Set(new(2026, 9, 7, 9, 30, 0, TimeSpan.Zero));
        check(planner.ActiveOccurrences().Count == 0, "Disabled schedules do not block");
        check(SchedulePlanner.Validate(morning with { Days = [] }, state.Blocklists) is not null, "Schedule requires selected days");
        check(SchedulePlanner.Validate(morning with { EndsAt = morning.StartsAt }, state.Blocklists) is not null, "Ambiguous zero-length schedule rejected");
        check(SchedulePlanner.Validate(morning with { BlocklistId = Guid.NewGuid() }, state.Blocklists) is not null, "Dangling blocklist reference rejected");
        check(SchedulePlanner.Validate(morning with { TimeZoneId = "Missing/Zone" }, state.Blocklists) is not null, "Unknown time zone rejected");

        clock.Set(new(2026, 9, 7, 8, 50, 0, TimeSpan.Zero));
        state.Schedules = [morning];
        session.Start(second.Apps, 30);
        clock.Set(new(2026, 9, 7, 9, 5, 0, TimeSpan.Zero));
        check(session.Session?.Apps.Count == 2, "Schedule activates during an existing manual session");
        clock.Set(new(2026, 9, 7, 9, 25, 0, TimeSpan.Zero));
        check(session.Expire() && session.Session?.Apps.Single().Path == first.Apps[0].Path, "Manual expiry preserves active scheduled blocking");
        clock.Set(new(2026, 9, 7, 13, 0, 0, TimeSpan.Zero));
        check(session.Session is null, "Sleep past all schedule deadlines releases all apps");

        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        state.Schedules = [morning with { Days = [DayOfWeek.Sunday], StartsAt = new(2, 30), EndsAt = new(4, 0), TimeZoneId = eastern.Id }];
        clock.Set(new(2026, 3, 8, 7, 0, 0, TimeSpan.Zero));
        check(planner.ActiveOccurrences().Single().EndsAt == new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero), "Spring DST gap advances to the first valid minute");
        state.Schedules = [state.Schedules[0] with { StartsAt = new(1, 15), EndsAt = new(1, 45) }];
        clock.Set(new(2026, 11, 1, 6, 30, 0, TimeSpan.Zero));
        check(planner.ActiveOccurrences().Single().EndsAt == new DateTimeOffset(2026, 11, 1, 6, 45, 0, TimeSpan.Zero), "Fall DST overlap covers both repeated clock times");
    }

    private sealed class CalendarClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Set(DateTimeOffset value) => current = value;
    }
}
