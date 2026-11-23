using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private readonly TimeLimitTracker timeLimits;
    private readonly DispatcherTimer timer;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private bool exiting, polling, wasBlocking, initialized;
    private int tickCount;
    private Blocklist? Selected => Lists.SelectedItem as Blocklist;

    public MainWindow(IStateStore store, IAppMonitor monitor, ITargetPolicy policy, IStartupRegistration? startup = null, bool background = false, IManagedAccountControl? managedAccount = null, IAppCatalog? catalog = null, IForegroundTracker? foreground = null)
    {
        InitializeComponent();
        this.store = store; this.monitor = monitor; this.policy = policy;
        this.startup = startup;
        state = store.Load(); session = new(state, TimeProvider.System);
        authentication = new(state, store, TimeProvider.System);
        timeLimits = new(state, TimeProvider.System, foreground ?? new FocusFence.Windows.WindowsForegroundTracker());
        Minutes.Text = state.SessionMinutes.ToString(); Delay.Text = state.UnlockSeconds.ToString();
        RefreshLists();
        Schedules.Initialize(state, Save, CanEdit, message => Status.Text = message);
        TimeLimits.Initialize(state, timeLimits, Save, EnsureAuthenticated, message => Status.Text = message, catalog);
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
        Pages.SelectionChanged += (_, _) => { if (Pages.SelectedItem is TabItem { Header: string h } && h.Contains("Time limits")) TimeLimits.Refresh(); };
        UpdateSession();
        UpdateAuthentication();
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
            var targets = new List<AppTarget>();
            targets.AddRange(state.LocalDisabledApps.Where(a => policy.GetRejection(a.Path) is null));
            if (active is not null) targets.AddRange(active.Apps);

            timeLimits.RecordActiveTime(1);
            var limitEval = timeLimits.Evaluate();
            foreach (var warning in limitEval.Warnings)
                tray.ShowBalloonTip(Defaults.NotificationMilliseconds, "Daily limit warning", $"{warning.Name} has 5 minutes remaining today.", Forms.ToolTipIcon.Warning);
            targets.AddRange(limitEval.Blocked.Where(a => policy.GetRejection(a.Path) is null));

            if (targets.Count > 0)
            {
                var distinct = targets.GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
                var errors = await Task.Run(() => monitor.Block(distinct, DateTimeOffset.MaxValue));
                if (errors.Count > 0) Status.Text = string.Join(" ", errors);
            }

            tickCount++;
            if ((tickCount % 15 == 0 || limitEval.Warnings.Count > 0 || limitEval.Blocked.Count > 0) &&
                Pages.SelectedItem is TabItem { Header: string h } && h.Contains("Time limits"))
                TimeLimits.Refresh();
        }
        catch (Exception ex) { Status.Text = "Blocking needs attention: " + ex.Message; }
        finally { polling = false; }
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
