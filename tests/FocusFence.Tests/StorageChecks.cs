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
        store.Save(state);
        check(store.Load().ManagedDraft == state.ManagedDraft, "Managed account and policy options survive settings reload");
        File.WriteAllText(path, "{}");
        check(store.Load().Schedules.Count == 0, "Legacy settings load with schedules disabled by default");
        check(store.Load().ManagedDraft == new ManagedPolicyDraft(), "Legacy settings receive an empty managed-account draft");
        clock.Advance(TimeSpan.FromMinutes(6));
        check(new SessionController(restored, clock).Expire(), "Expired saved session reconciled after restart");
        File.WriteAllText(path, "{broken");
        try { store.Load(); throw new Exception("Corrupt settings accepted"); }
        catch (System.Text.Json.JsonException) { check(File.ReadAllText(path) == "{broken", "Corrupt settings preserved, not silently overwritten"); }
    }
}
