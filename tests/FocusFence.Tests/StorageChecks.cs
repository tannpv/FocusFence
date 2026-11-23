using System.IO;
using FocusFence.Core;
using FocusFence.Windows;

namespace FocusFence.Tests;

internal static class StorageChecks
{
    public static void Run(Action<bool, string> check, SessionController controller, AppState state, TestClock clock, string temp)
    {
        var path = Path.Combine(temp, "settings.json");
        var store = new JsonStateStore(path);
        controller.Start([new("Example", @"C:\Example\app.exe")], 5);
        store.Save(state);
        var restored = store.Load();
        check(restored.Session!.EndsAt == state.Session!.EndsAt, "Session deadline survives storage round trip");
        state.Schedules.Add(new(Guid.NewGuid(), "Saved schedule", state.Blocklists[0].Id, [DayOfWeek.Monday], new(9, 0), new(17, 0), TimeZoneInfo.Local.Id, true));
        state.SkippedSchedules[state.Schedules[0].Id] = clock.GetUtcNow().AddHours(1);
        store.Save(state);
        var scheduledState = store.Load();
        check(scheduledState.Schedules.Single().StartsAt == new TimeOnly(9, 0) && scheduledState.SkippedSchedules.Count == 1, "Schedules and skipped periods survive JSON persistence");
        state.ManagedDraft = new("TestStandardUser", true, false);
        state.TimeLimits.Add(new(@"C:\Games\stored.exe", "Stored Game", 45, true));
        state.DailyUsages.Add(new() { Date = DateOnly.FromDateTime(DateTime.UtcNow), Path = @"C:\Games\stored.exe", UsedSeconds = 600 });
        store.Save(state);
        var loadedState = store.Load();
        check(loadedState.ManagedDraft == state.ManagedDraft, "Managed account and policy options survive settings reload");
        check(loadedState.TimeLimits.Single().DailyMinutes == 45 && loadedState.DailyUsages.Single().UsedSeconds == 600, "Time limits and daily usages survive JSON persistence");
        File.WriteAllText(path, "{}");
        check(store.Load().Schedules.Count == 0, "Legacy settings load with schedules disabled by default");
        check(store.Load().ManagedDraft == new ManagedPolicyDraft(), "Legacy settings receive an empty managed-account draft");
        File.WriteAllText(path, """{"TimeLimits": [{"Path": "C:\\Games\\bad.exe", "Name": "Bad", "DailyMinutes": 0}]}""");
        try { store.Load(); throw new Exception("Invalid time limit accepted"); }
        catch (InvalidDataException) { check(true, "Corrupted time limit values rejected on load"); }
        File.WriteAllText(path, """{"TimeLimits": [{"Path": "C:\\Games\\dup.exe", "Name": "One", "DailyMinutes": 30}, {"Path": "c:\\games\\dup.exe", "Name": "Two", "DailyMinutes": 60}]}""");
        try { store.Load(); throw new Exception("Duplicate time limit paths accepted"); }
        catch (InvalidDataException) { check(true, "Duplicate time limit executable paths rejected on load"); }
        clock.Advance(TimeSpan.FromMinutes(6));
        check(new SessionController(restored, clock).Expire(), "Expired saved session reconciled after restart");
        File.WriteAllText(path, "{broken");
        try { store.Load(); throw new Exception("Corrupt settings accepted"); }
        catch (System.Text.Json.JsonException) { check(File.ReadAllText(path) == "{broken", "Corrupt settings preserved, not silently overwritten"); }
    }
}
