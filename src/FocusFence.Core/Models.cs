namespace FocusFence.Core;

public static class Defaults
{
    public const int SessionMinutes = 25;
    public const int UnlockSeconds = 15;
    public const int PollMilliseconds = 1000;
    public const int NotificationMilliseconds = 3000;
    public const int MaximumSessionMinutes = 1440;
    public static readonly TimeOnly ScheduleStart = new(9, 0);
    public static readonly TimeOnly ScheduleEnd = new(17, 0);
}

public sealed record AppTarget(string Name, string Path);
public sealed record Blocklist(Guid Id, string Name, List<AppTarget> Apps);
public sealed record FocusSession(DateTimeOffset EndsAt, List<AppTarget> Apps);
public sealed record ManagedPolicyDraft(string AccountName = "", bool RestrictNewApps = false, bool BlockUninstallers = false);
public sealed class AppState
{
    public int SessionMinutes { get; set; } = Defaults.SessionMinutes;
    public int UnlockSeconds { get; set; } = Defaults.UnlockSeconds;
    public List<Blocklist> Blocklists { get; set; } = [new(Guid.NewGuid(), "Focus", [])];
    public FocusSession? Session { get; set; }
    public List<FocusSchedule> Schedules { get; set; } = [];
    public Dictionary<Guid, DateTimeOffset> SkippedSchedules { get; set; } = [];
    public PinProfile? Pin { get; set; }
    public List<AppTarget> ManagedDisabledApps { get; set; } = [];
    public ManagedPolicyDraft ManagedDraft { get; set; } = new();
}

public interface IStateStore
{
    AppState Load();
    void Save(AppState state);
}

public interface IAppMonitor
{
    IReadOnlyList<AppTarget> GetRunningApps();
    IReadOnlyList<string> Block(IReadOnlyList<AppTarget> targets, DateTimeOffset endsAt);
}

public interface ITargetPolicy
{
    string? GetRejection(string path);
}
