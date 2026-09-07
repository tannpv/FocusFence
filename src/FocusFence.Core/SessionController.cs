namespace FocusFence.Core;

public sealed class SessionController(AppState state, TimeProvider clock)
{
    private DateTimeOffset? unlockAt;
    private readonly SchedulePlanner schedules = new(state, clock);
    public bool HasActiveSchedules => schedules.ActiveOccurrences().Count > 0;
    public FocusSession? Session
    {
        get
        {
            var windows = schedules.ActiveOccurrences()
                .Select(s => new FocusSession(s.EndsAt, s.Apps.ToList())).ToList();
            if (state.Session is { } manual && manual.EndsAt > clock.GetUtcNow()) windows.Add(manual);
            if (windows.Count == 0) return null;
            return new(windows.Min(w => w.EndsAt), windows.SelectMany(w => w.Apps)
                .DistinctBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }
    public TimeSpan Remaining => Session is { } session
        ? TimeSpan.FromTicks(Math.Max(0, (session.EndsAt - clock.GetUtcNow()).Ticks)) : TimeSpan.Zero;
    public TimeSpan UnlockRemaining => unlockAt is { } time
        ? TimeSpan.FromTicks(Math.Max(0, (time - clock.GetUtcNow()).Ticks)) : TimeSpan.Zero;
    public bool UnlockRequested => unlockAt.HasValue;

    public void Start(IEnumerable<AppTarget> apps, int minutes)
    {
        if (Session is not null) throw new InvalidOperationException("A session is already active.");
        if (minutes is < 1 or > Defaults.MaximumSessionMinutes)
            throw new ArgumentException($"Choose 1–{Defaults.MaximumSessionMinutes} minutes.");
        var snapshot = apps.DistinctBy(app => app.Path, StringComparer.OrdinalIgnoreCase).ToList();
        if (snapshot.Count == 0) throw new InvalidOperationException("Add at least one app first.");
        state.Session = new(clock.GetUtcNow().AddMinutes(minutes), snapshot);
        unlockAt = null;
    }

    public bool Expire()
    {
        var expired = state.Session is { } manual && manual.EndsAt <= clock.GetUtcNow();
        if (expired) state.Session = null;
        if (Session is null) unlockAt = null;
        return expired;
    }

    public void RequestUnlock(int seconds)
    {
        if (Session is null) throw new InvalidOperationException("No active session.");
        if (seconds is < 1 or > 300) throw new ArgumentException("Unlock delay must be 1–300 seconds.");
        unlockAt ??= clock.GetUtcNow().AddSeconds(seconds);
    }

    public void CompleteUnlock()
    {
        if (!UnlockRequested || UnlockRemaining > TimeSpan.Zero)
            throw new InvalidOperationException("Wait for the unlock delay to finish.");
        DismissCurrent();
    }

    /// <summary>Decline restoring current work, or finish an approved emergency unlock.</summary>
    public void DismissCurrent()
    {
        schedules.SkipCurrentOccurrences();
        state.Session = null;
        unlockAt = null;
    }
}
