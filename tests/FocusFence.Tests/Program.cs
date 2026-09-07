using System.Diagnostics;
using FocusFence.Core;
using FocusFence.Windows;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++; Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}

var clock = new TestClock();
var state = new AppState();
var controller = new SessionController(state, clock);
var targets = new List<AppTarget> { new("Example", @"C:\Example\app.exe"), new("Duplicate", @"c:\example\APP.exe") };
Reject(() => controller.Start([], 25), "Empty blocklist rejected");
Reject(() => controller.Start(targets, 0), "Invalid duration rejected");
controller.Start(targets, 25);
Check(controller.Session!.Apps.Count == 1, "Executable paths matched case insensitively");
targets.Clear();
Check(controller.Session.Apps.Count == 1, "Session snapshots the blocklist");
Reject(() => controller.Start(controller.Session.Apps, 5), "Concurrent session rejected");
clock.Advance(TimeSpan.FromMinutes(24));
Check(!controller.Expire() && controller.Remaining == TimeSpan.FromMinutes(1), "Session stays active until deadline");
clock.Advance(TimeSpan.FromHours(2));
Check(controller.Expire() && state.Session is null, "Sleep past deadline expires the session");
controller.Start([new("Example", @"C:\Example\app.exe")], 25);
controller.RequestUnlock(15);
Reject(controller.CompleteUnlock, "Early emergency unlock rejected");
clock.Advance(TimeSpan.FromSeconds(10));
controller.RequestUnlock(15);
Check(controller.UnlockRemaining == TimeSpan.FromSeconds(5), "Repeated unlock request does not reset delay");
clock.Advance(TimeSpan.FromSeconds(5));
controller.CompleteUnlock();
Check(state.Session is null, "Unlock ends session after delay");

var temp = Path.Combine(Path.GetTempPath(), "FocusFence-tests-" + Guid.NewGuid());
ScheduleChecks.Run(Check);
PinChecks.Run(Check);
foreach (var outcome in Enum.GetValues<ManagedPolicyOutcome>())
    Check(WindowsManagedAccountControl.InterpretExitCode((int)outcome) == outcome, $"Administrator protocol distinguishes {outcome}");
Reject(() => WindowsManagedAccountControl.InterpretExitCode(0), "Ordinary process exit cannot falsely report policy applied");
Reject(() => WindowsManagedAccountControl.InterpretExitCode(1), "Administrator failure exit does not report success");
Reject(() => WindowsManagedAccountControl.InterpretExitCode(999), "Unknown administrator result fails closed");
Reject(() => new ManagedAccountRequest("", true, false).Validate(), "Managed policy requires an account name");
Reject(() => new ManagedAccountRequest("DOMAIN\\User", true, false).Validate(), "Managed policy rejects domain-qualified input");
Reject(() => new ManagedAccountRequest("TestUser", false, false).Validate(), "Managed policy requires a restriction or explicit restore");
Check(WindowsStartupRegistration.BuildCommand(@"C:\Program Files\FocusFence\FocusFence.App.exe") == "\"C:\\Program Files\\FocusFence\\FocusFence.App.exe\" --startup", "Startup executable path is quoted and includes background argument");
Reject(() => WindowsStartupRegistration.BuildCommand("relative.exe"), "Startup rejects relative paths");
Reject(() => WindowsStartupRegistration.BuildCommand(@"C:\Apps\bad" + '"' + ".exe"), "Startup rejects embedded quotes");
Reject(() => WindowsStartupRegistration.BuildCommand(@"C:\" + new string('a', 260) + ".exe"), "Startup rejects commands exceeding Windows length limit");
Directory.CreateDirectory(temp);
try
{
    var path = Path.Combine(temp, "settings.json");
    var store = new JsonStateStore(path);
    controller.Start([new("Example", @"C:\Example\app.exe")], 5);
    store.Save(state);
    var restored = store.Load();
    Check(restored.Session!.EndsAt == state.Session!.EndsAt, "Session deadline survives storage round trip");
    state.Schedules.Add(new(Guid.NewGuid(), "Saved schedule", state.Blocklists[0].Id, [DayOfWeek.Monday], new(9, 0), new(17, 0), TimeZoneInfo.Local.Id, true));
    state.SkippedSchedules[state.Schedules[0].Id] = clock.GetUtcNow().AddHours(1);
    store.Save(state);
    var scheduledState = store.Load();
    Check(scheduledState.Schedules.Single().StartsAt == new TimeOnly(9, 0) && scheduledState.SkippedSchedules.Count == 1, "Schedules and skipped periods survive JSON persistence");
    state.ManagedDraft = new("TestStandardUser", true, false);
    store.Save(state);
    Check(store.Load().ManagedDraft == state.ManagedDraft, "Managed account and policy options survive settings reload");
    File.WriteAllText(path, "{}");
    Check(store.Load().Schedules.Count == 0, "Legacy settings load with schedules disabled by default");
    Check(store.Load().ManagedDraft == new ManagedPolicyDraft(), "Legacy settings receive an empty managed-account draft");
    clock.Advance(TimeSpan.FromMinutes(6));
    Check(new SessionController(restored, clock).Expire(), "Expired saved session reconciled after restart");
    File.WriteAllText(path, "{broken");
    try { store.Load(); throw new Exception("Corrupt settings accepted"); }
    catch (System.Text.Json.JsonException) { Check(File.ReadAllText(path) == "{broken", "Corrupt settings preserved, not silently overwritten"); }

    var policy = new WindowsTargetPolicy();
    if (args.Contains("--catalog"))
    {
        var discovered = new WindowsAppCatalog(new WindowsAppMonitor(policy), policy).Discover();
        Check(discovered.Count > 0 && discovered.All(a => Path.IsPathFullyQualified(a.Path) && File.Exists(a.Path)), "Windows desktop catalog discovers existing executable paths without launching apps");
        Check(discovered.Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == discovered.Count, "Windows catalog merges duplicate executable entries");
        Console.WriteLine($"Discovered {discovered.Count} desktop apps.");
    }
    Check(policy.GetRejection(Environment.ProcessPath!) is not null, "Blocker executable protected");
    Check(policy.GetRejection(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) is not null, "Windows executables protected");
    Check(policy.GetRejection("relative.exe") is not null, "Relative executable paths rejected");
    if (args.Length > 0)
    {
        var probe = Path.GetFullPath(args[0]);
        var monitor = new WindowsAppMonitor(policy);
        using var process = Process.Start(new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            Thread.Sleep(250);
            monitor.Block([new("Probe", Path.Combine(temp, Path.GetFileName(probe)))], DateTimeOffset.UtcNow.AddMinutes(1));
            Check(!process.HasExited, "Same filename at another path is not terminated");
            monitor.Block([new("Probe", probe)], DateTimeOffset.UtcNow.AddSeconds(-1));
            Check(!process.HasExited, "Expired session cannot terminate an app");
            var errors = monitor.Block([new("Probe", probe)], DateTimeOffset.UtcNow.AddMinutes(1));
            Check(process.WaitForExit(5000) && errors.Count == 0, "Exact selected executable is terminated");
        }
        finally { if (!process.HasExited) process.Kill(); }
    }
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"All {checks} checks passed.");

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
