using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace FocusFence.App;

public partial class MainWindow
{
    private void OnWindowsSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
            Dispatcher.BeginInvoke(new Action(() => { authentication.Lock(); UpdateAuthentication(); }));
    }

    private void UpdateAuthentication()
    {
        Pages.IsEnabled = authentication.IsUnlocked;
        LockedCover.Visibility = authentication.IsUnlocked ? Visibility.Collapsed : Visibility.Visible;
        LockButton.Content = !authentication.IsConfigured ? "Set PIN" : authentication.IsUnlocked ? "Lock" : "Sign in";
    }

    private bool EnsureAuthenticated()
    {
        if (authentication.IsUnlocked) return true;
        var dialog = new PinDialog(authentication, configure: false) { Owner = this };
        var accepted = dialog.ShowDialog() == true;
        UpdateAuthentication(); return accepted;
    }

    private void SignIn(object sender, RoutedEventArgs e) => EnsureAuthenticated();

    private void LockOrConfigure(object sender, RoutedEventArgs e)
    {
        if (!authentication.IsConfigured) ConfigurePin(sender, e);
        else if (!authentication.IsUnlocked) EnsureAuthenticated();
        else { authentication.Lock(); UpdateAuthentication(); }
    }

    private void ConfigurePin(object sender, RoutedEventArgs e)
    {
        if (authentication.IsConfigured) authentication.Lock();
        if (!EnsureAuthenticated()) return;
        new PinDialog(authentication, configure: true) { Owner = this }.ShowDialog();
        UpdateAuthentication();
    }

    private void RefreshStartup()
    {
        StartupEnabled.IsEnabled = startup is not null;
        try { StartupEnabled.IsChecked = startup?.IsEnabled() == true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { StartupEnabled.IsEnabled = false; Status.Text = "Could not read startup registration: " + ex.Message; }
    }

    private void SaveStartup(object sender, RoutedEventArgs e)
    {
        if (startup is null || !CanEdit()) return;
        var enabled = StartupEnabled.IsChecked == true;
        try
        {
            startup.SetEnabled(enabled);
            RefreshStartup();
            Status.Text = enabled ? "Startup enabled. Keep this app folder in place. Focus will resume automatically at sign-in." : "Startup disabled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { RefreshStartup(); Status.Text = "Could not change startup registration: " + ex.Message; }
    }

    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        if (!int.TryParse(Delay.Text, out var seconds) || seconds is < 1 or > 300) { Status.Text = "Choose an unlock delay of 1–300 seconds."; return; }
        state.UnlockSeconds = seconds; if (Save()) Status.Text = "Settings saved.";
    }

    private bool Save()
    {
        try { store.Save(state); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status.Text = "Could not save settings: " + ex.Message; return false; }
    }

    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!exiting) { e.Cancel = true; authentication.Lock(); UpdateAuthentication(); Hide(); } }
    private void ExitApp()
    {
        if (!EnsureAuthenticated()) return;
        if (polling) { Status.Text = "Please try Exit again after the current blocking check."; ShowWindow(); return; }
        if (session.Session is not null && MessageBox.Show("Exit and stop blocking? Your saved session can be resumed when you reopen FocusFence.", "Exit FocusFence", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        exiting = true; Close();
    }
}
