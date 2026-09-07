using System.Text.Json;
using FocusFence.Core;

namespace FocusFence.Windows;

public sealed class JsonStateStore(string path) : IStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public AppState Load()
    {
        if (!File.Exists(path)) return new();
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Settings are empty.");
        if (state.Pin is { } pin && !PinAuthentication.ValidProfile(pin))
            throw new InvalidDataException("PIN settings are invalid. Restore a valid settings backup.");
        if (state.ManagedDisabledApps is null || state.ManagedDisabledApps.Any(a => a is null || string.IsNullOrWhiteSpace(a.Path)))
            throw new InvalidDataException("Managed app settings contain an invalid executable.");
        if (state.ManagedDraft is null || state.ManagedDraft.AccountName is null)
            throw new InvalidDataException("Managed account draft is invalid.");
        if (state.Blocklists is null || state.Blocklists.Any(x => x is null || x.Apps is null || x.Apps.Any(a => a is null || string.IsNullOrWhiteSpace(a.Path))) ||
            state.SessionMinutes is < 1 or > Defaults.MaximumSessionMinutes || state.UnlockSeconds is < 1 or > 300 ||
            (state.Session is { } session && (session.Apps is null || session.Apps.Any(a => a is null || string.IsNullOrWhiteSpace(a.Path)))))
            throw new InvalidDataException("Settings contain invalid values. Restore or rename the settings file.");
        if (state.Schedules is null || state.SkippedSchedules is null ||
            state.Schedules.Any(s => s is null || SchedulePlanner.Validate(s, state.Blocklists) is not null) ||
            state.Schedules.Select(s => s.Id).Distinct().Count() != state.Schedules.Count)
            throw new InvalidDataException("Settings contain invalid schedules. Restore or rename the settings file.");
        return state;
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
