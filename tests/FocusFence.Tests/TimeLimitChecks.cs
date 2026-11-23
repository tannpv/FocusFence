using FocusFence.Core;

internal static class TimeLimitChecks
{
    public static void Run(Action<bool, string> check)
    {
        var clock = new TestClock(new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
        var foreground = new TestForeground();
        var state = new AppState
        {
            TimeLimits =
            [
                new(@"C:\Games\roblox.exe", "Roblox", 60, true),
                new(@"C:\Games\minecraft.exe", "Minecraft", 30, false)
            ]
        };

        var tracker = new TimeLimitTracker(state, clock, foreground);

        // 1. Initial state has zero usage
        var robloxStatus = tracker.GetStatus(@"C:\Games\roblox.exe")!;
        check(robloxStatus.UsedSeconds == 0 && robloxStatus.RemainingSeconds == 3600 && !robloxStatus.IsExhausted, "Initial app usage is zero with full daily limit remaining");

        // 2. Active foreground tracking
        foreground.ActivePath = @"C:\Games\roblox.exe";
        tracker.RecordActiveTime(10);
        robloxStatus = tracker.GetStatus(@"C:\Games\roblox.exe")!;
        check(robloxStatus.UsedSeconds == 10 && robloxStatus.RemainingSeconds == 3590, "Active foreground app accumulates elapsed seconds");

        // 3. Different app in foreground does not accumulate
        foreground.ActivePath = @"C:\Windows\explorer.exe";
        tracker.RecordActiveTime(20);
        robloxStatus = tracker.GetStatus(@"C:\Games\roblox.exe")!;
        check(robloxStatus.UsedSeconds == 10, "Non-monitored foreground app does not accumulate time");

        // 4. Disabled limit does not accumulate
        foreground.ActivePath = @"C:\Games\minecraft.exe";
        tracker.RecordActiveTime(15);
        var mcStatus = tracker.GetStatus(@"C:\Games\minecraft.exe");
        check(mcStatus is null || mcStatus.UsedSeconds == 0, "Disabled limit app does not accumulate time");

        // 5. 5-minute warning triggers once
        foreground.ActivePath = @"C:\Games\roblox.exe";
        tracker.RecordActiveTime(3590 - 300); // 3300 seconds total (remaining = 300s = 5m)
        var evaluation = tracker.Evaluate();
        check(evaluation.Warnings.Any(w => w.Path.Equals(@"C:\Games\roblox.exe", StringComparison.OrdinalIgnoreCase)), "Warning emitted at 5 minutes remaining");
        check(!evaluation.Blocked.Any(b => b.Path.Equals(@"C:\Games\roblox.exe", StringComparison.OrdinalIgnoreCase)), "App is not yet blocked at 5 minutes remaining");

        // Repeated evaluate does not re-emit warning
        var eval2 = tracker.Evaluate();
        check(!eval2.Warnings.Any(w => w.Path.Equals(@"C:\Games\roblox.exe", StringComparison.OrdinalIgnoreCase)), "Warning is not re-emitted on subsequent evaluation");

        // 6. Expiry blocks the app
        tracker.RecordActiveTime(300); // reaches 3600s
        var evalBlocked = tracker.Evaluate();
        check(evalBlocked.Blocked.Any(b => b.Path.Equals(@"C:\Games\roblox.exe", StringComparison.OrdinalIgnoreCase)), "App is blocked once daily limit is exhausted");
        check(tracker.GetStatus(@"C:\Games\roblox.exe")!.IsExhausted, "App status reports exhausted");

        // 7. Parent bonus minutes unblocks the app
        tracker.GrantBonusMinutes(@"C:\Games\roblox.exe", 15);
        var evalBonus = tracker.Evaluate();
        check(!evalBonus.Blocked.Any(b => b.Path.Equals(@"C:\Games\roblox.exe", StringComparison.OrdinalIgnoreCase)), "Granting bonus minutes unblocks the app");
        check(tracker.GetStatus(@"C:\Games\roblox.exe")!.RemainingSeconds == 900, "Bonus minutes added to remaining time");

        // 8. Midnight rollover resets usage for the new calendar day
        clock.Advance(TimeSpan.FromDays(1)); // Next day
        var nextDayStatus = tracker.GetStatus(@"C:\Games\roblox.exe")!;
        check(nextDayStatus.UsedSeconds == 0 && nextDayStatus.RemainingSeconds == 3600 && !nextDayStatus.IsExhausted, "Midnight rollover resets daily usage to full allowance");

        // 9. Old usage records are pruned
        clock.Advance(TimeSpan.FromDays(20));
        tracker.PruneOldUsages(14);
        check(state.DailyUsages.All(u => u.Date >= DateOnly.FromDateTime(clock.GetUtcNow().DateTime).AddDays(-14)), "Usage history older than 14 days is pruned");
    }

    private sealed class TestForeground : IForegroundTracker
    {
        public string? ActivePath { get; set; }
        public string? GetActiveExecutablePath() => ActivePath;
    }

    private sealed class TestClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset current = initial;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }
}
