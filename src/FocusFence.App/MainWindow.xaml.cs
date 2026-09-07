using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FocusFence.Core;
using Forms = System.Windows.Forms;

namespace FocusFence.App;

public partial class MainWindow : Window
{
    private readonly IStateStore store;
    private readonly IAppMonitor monitor;
    private readonly ITargetPolicy policy;
    private readonly IStartupRegistration? startup;
    private readonly AppState state;
    private readonly SessionController session;
    private readonly PinAuthentication authentication;
    private readonly DispatcherTimer timer;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private bool exiting;
    private bool polling;
    private bool wasBlocking;
    private bool initialized;
    private Blocklist? Selected => Lists.SelectedItem as Blocklist;

    public MainWindow(IStateStore store, IAppMonitor monitor, ITargetPolicy policy, IStartupRegistration? startup = null, bool background = false, IManagedAccountControl? managedAccount = null, IAppCatalog? catalog = null)
    {
        InitializeComponent();
        this.store = store; this.monitor = monitor; this.policy = policy;
        this.startup = startup;
        state = store.Load(); session = new(state, TimeProvider.System);
        authentication = new(state, store, TimeProvider.System);
        Minutes.Text = state.SessionMinutes.ToString(); Delay.Text = state.UnlockSeconds.ToString();
        RefreshLists();
        Schedules.Initialize(state, Save, CanEdit, message => Status.Text = message);
        ManagedAccount.Initialize(managedAccount, state, EnsureAuthenticated, Save);
        AppAccess.Initialize(state, catalog, policy, Save, EnsureAuthenticated);
        ManagedAccount.DraftChanged += (_, _) => AppAccess.RefreshSummary();
        AppAccess.ReviewRequested += (_, _) => Pages.SelectedItem = ManagedAccountTab;
        RefreshStartup();
        using (var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/FocusFence.App;component/Assets/FocusFence.ico")).Stream)
        using (var sourceIcon = new System.Drawing.Icon(iconStream, 32, 32))
            trayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "FocusFence", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open FocusFence", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowWindow();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Defaults.PollMilliseconds) };
        timer.Tick += async (_, _) => await Tick();
        Loaded += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            if (background) { Hide(); Opacity = 1; ShowInTaskbar = true; }
            if (session.Expire()) Save();
            if (!background && authentication.IsUnlocked && session.Session is not null && MessageBox.Show("Resume current focus sessions and scheduled hours? Selected apps will be closed. Save open work before continuing. Choosing No skips the currently active scheduled periods.", "Resume focus", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            { session.DismissCurrent(); Save(); }
            await Tick(); timer.Start();
        };
        Closing += OnClosing;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnWindowsSessionSwitch;
        Closed += (_, _) => { timer.Stop(); tray.Dispose(); trayIcon.Dispose(); Microsoft.Win32.SystemEvents.SessionSwitch -= OnWindowsSessionSwitch; };
        UpdateSession();
        UpdateAuthentication();
    }

    private void OnWindowsSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
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

    private bool Save()
    {
        try { store.Save(state); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status.Text = "Could not save settings: " + ex.Message; return false; }
    }
    private void RefreshLists(Guid? selected = null)
    {
        Lists.ItemsSource = null; Lists.ItemsSource = state.Blocklists;
        Lists.SelectedItem = state.Blocklists.FirstOrDefault(x => x.Id == selected) ?? state.Blocklists.FirstOrDefault();
        RefreshTargets();
        Schedules.Refresh();
    }
    private void SelectList(object sender, SelectionChangedEventArgs e) { if (Targets is not null) RefreshTargets(); }
    private void RefreshTargets() { Targets.ItemsSource = null; Targets.ItemsSource = Selected?.Apps; ListName.Text = Selected?.Name ?? ""; }
    private bool CanEdit()
    {
        if (!EnsureAuthenticated()) return false;
        if (session.Session is null) return true;
        Status.Text = "Finish or unlock your session before changing blocklists or settings."; return false;
    }
    private void NewList(object sender, RoutedEventArgs e)
    {
        if (!CanEdit() || string.IsNullOrWhiteSpace(ListName.Text)) return;
        var list = new Blocklist(Guid.NewGuid(), ListName.Text.Trim(), []);
        state.Blocklists.Add(list); Save(); RefreshLists(list.Id);
    }
    private void RenameList(object sender, RoutedEventArgs e)
    {
        if (!CanEdit() || Selected is not { } list || string.IsNullOrWhiteSpace(ListName.Text)) return;
        state.Blocklists[state.Blocklists.IndexOf(list)] = list with { Name = ListName.Text.Trim() }; Save(); RefreshLists(list.Id);
    }
    private void DeleteList(object sender, RoutedEventArgs e)
    {
        if (!CanEdit() || Selected is not { } list) return;
        if (state.Schedules.Any(s => s.BlocklistId == list.Id)) { Status.Text = "Delete or reassign schedules using this blocklist first."; return; }
        if (MessageBox.Show($"Delete '{list.Name}'?", "Delete blocklist", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        state.Blocklists.Remove(list); Save(); RefreshLists();
    }
    private void BrowseApp(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) AddTarget(new(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName));
    }
    private void AddTarget(AppTarget app)
    {
        if (!CanEdit()) return;
        if (Selected is not { } list) { Status.Text = "Create a blocklist first."; return; }
        try
        {
            if (policy.GetRejection(app.Path) is { } rejection) { Status.Text = rejection; return; }
            if (!File.Exists(app.Path)) { Status.Text = "That executable no longer exists."; return; }
            if (!list.Apps.Any(x => string.Equals(x.Path, app.Path, StringComparison.OrdinalIgnoreCase))) list.Apps.Add(app);
            Save(); RefreshTargets();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Status.Text = ex.Message; }
    }
    private void RemoveApp(object sender, RoutedEventArgs e)
    {
        if (!CanEdit() || Targets.SelectedItem is not AppTarget app) return;
        Selected?.Apps.Remove(app); Save(); RefreshTargets();
    }
    private async void RefreshApps(object sender, RoutedEventArgs e)
    {
        if (!EnsureAuthenticated()) return;
        try { Running.ItemsSource = await Task.Run(monitor.GetRunningApps); Running.SelectedIndex = 0; Status.Text = "Running apps refreshed."; }
        catch (Exception ex) { Status.Text = "Could not list apps: " + ex.Message; }
    }
    private void AddRunning(object sender, RoutedEventArgs e) { if (Running.SelectedItem is AppTarget app) AddTarget(app); }
    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        if (!int.TryParse(Delay.Text, out var seconds) || seconds is < 1 or > 300) { Status.Text = "Choose an unlock delay of 1–300 seconds."; return; }
        state.UnlockSeconds = seconds; if (Save()) Status.Text = "Settings saved.";
    }
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
    private async Task Tick()
    {
        if (polling) return;
        polling = true;
        try
        {
            if (session.Expire()) Save();
            var active = session.Session;
            if (wasBlocking && active is null)
            {
                Status.Text = "Focus complete. Your apps are available again.";
                tray.ShowBalloonTip(Defaults.NotificationMilliseconds, "Focus complete", "Your apps are available again.", Forms.ToolTipIcon.Info);
            }
            if (!wasBlocking && active is not null && session.HasActiveSchedules)
                tray.ShowBalloonTip(Defaults.NotificationMilliseconds, "Scheduled focus", "Scheduled hours are active. Selected apps are being blocked.", Forms.ToolTipIcon.Info);
            wasBlocking = active is not null;
            UpdateSession();
            var localApps = state.LocalDisabledApps.Where(a => policy.GetRejection(a.Path) is null).ToArray();
            if (localApps.Length > 0)
            {
                var localErrors = await Task.Run(() => monitor.Block(localApps, DateTimeOffset.MaxValue));
                if (localErrors.Count > 0) Status.Text = string.Join(" ", localErrors);
            }
            if (active is not null)
            {
                var errors = await Task.Run(() => monitor.Block(active.Apps, active.EndsAt));
                if (errors.Count > 0) Status.Text = string.Join(" ", errors);
            }
        }
        catch (Exception ex) { Status.Text = "Blocking needs attention: " + ex.Message; }
        finally { polling = false; }
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
    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!exiting) { e.Cancel = true; authentication.Lock(); UpdateAuthentication(); Hide(); } }
    private void ExitApp()
    {
        if (!EnsureAuthenticated()) return;
        if (polling) { Status.Text = "Please try Exit again after the current blocking check."; ShowWindow(); return; }
        if (session.Session is not null && MessageBox.Show("Exit and stop blocking? Your saved session can be resumed when you reopen FocusFence.", "Exit FocusFence", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        exiting = true; Close();
    }
}
