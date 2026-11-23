using System;
using System.Linq;
using System.Windows;
using FocusFence.Core;

namespace FocusFence.App;

public partial class MainWindow
{
    private void StartSession(object sender, RoutedEventArgs e)
    {
        if (!EnsureAuthenticated()) return;
        if (session.Session is not null) return;
        if (!int.TryParse(Minutes.Text, out var minutes) || minutes is < 1 or > Defaults.MaximumSessionMinutes)
        { Status.Text = $"Choose 1–{Defaults.MaximumSessionMinutes} minutes."; return; }
        if (Selected is not { Apps.Count: > 0 } list) { Status.Text = "Add apps to a blocklist first."; return; }
        var names = string.Join("\n", list.Apps.Select(a => "• " + a.Name));
        if (MessageBox.Show($"Save your work before continuing. These apps will be closed now and whenever they launch during the session:\n\n{names}\n\nUnsaved changes may be lost. Start?", "Start focus session", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (session.Session is not null) { Status.Text = "A schedule started while the confirmation was open. Unlock it before starting another session."; return; }
        session.Start(list.Apps, minutes); state.SessionMinutes = minutes;
        if (!Save()) { state.Session = null; UpdateSession(); return; }
        Status.Text = "Focus session started."; UpdateSession();
    }

    private void UnlockSession(object sender, RoutedEventArgs e)
    {
        if (!EnsureAuthenticated()) return;
        if (polling) return;
        if (session.Session is null) { session.Expire(); UpdateSession(); return; }
        if (!session.UnlockRequested) session.RequestUnlock(state.UnlockSeconds);
        else if (session.UnlockRemaining == TimeSpan.Zero) { session.CompleteUnlock(); Save(); Status.Text = "Focus ended. Current scheduled periods are skipped; future periods remain enabled."; }
        UpdateSession();
    }

    private void UpdateSession()
    {
        var current = session.Session;
        var active = current is not null;
        var remaining = session.Remaining;
        TimerText.Text = active ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}" : "Ready when you are";
        SessionText.Text = active ? $"Blocking {current!.Apps.Count} selected apps." + (session.HasActiveSchedules ? " Scheduled focus is active; timer shows the next ending period." : "") : "Choose a blocklist, then take back your attention.";
        StartButton.IsEnabled = !active; Minutes.IsEnabled = !active; Lists.IsEnabled = !active;
        UnlockButton.IsEnabled = active;
        UnlockButton.Content = !session.UnlockRequested ? "Emergency unlock" : session.UnlockRemaining > TimeSpan.Zero ? $"Wait {Math.Ceiling(session.UnlockRemaining.TotalSeconds)}s…" : "End session now";
        tray.Text = active ? "FocusFence • " + TimerText.Text : "FocusFence • Ready";
    }
}
