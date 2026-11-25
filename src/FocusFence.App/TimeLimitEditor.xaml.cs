using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusFence.Core;
using Microsoft.Win32;

namespace FocusFence.App;

public partial class TimeLimitEditor : UserControl
{
    private AppState? state;
    private TimeLimitTracker? tracker;
    private IAppCatalog? catalog;
    private Func<bool> save = () => false;
    private Func<bool> authenticate = () => false;
    private Action<string> status = _ => { };

    public TimeLimitEditor() { InitializeComponent(); Loaded += (_, _) => RefreshAvailableApps(); }

    public void Initialize(AppState appState, TimeLimitTracker limitTracker, Func<bool> saveState, Func<bool> auth, Action<string> showStatus, IAppCatalog? appCatalog = null)
    {
        state = appState; tracker = limitTracker; save = saveState; authenticate = auth; status = showStatus; catalog = appCatalog;
        Refresh(); RefreshAvailableApps();
    }

    public async void RefreshAvailableApps()
    {
        if (catalog is null) return;
        try { AppPicker.ItemsSource = (await Task.Run(catalog.Discover)).Where(a => a.Restriction is null).OrderBy(a => a.Name).ToList(); }
        catch { }
    }

    private void OnAppPicked(object sender, SelectionChangedEventArgs e)
    {
        if (AppPicker.SelectedItem is not CatalogApp app) return;
        LimitName.Text = app.Name; LimitPath.Text = app.Path;
        if (string.IsNullOrWhiteSpace(LimitMinutes.Text)) LimitMinutes.Text = "60";
        if (app.Path.EndsWith("launcher.exe", StringComparison.OrdinalIgnoreCase) || app.Path.EndsWith("hyp.exe", StringComparison.OrdinalIgnoreCase))
            status("Note: Launchers may start separate games. If time is not tracked, select the game .exe directly.");
    }

    public void Refresh()
    {
        if (state is null || tracker is null) return;
        LimitsList.ItemsSource = state.TimeLimits.Select(limit =>
        {
            var st = tracker.GetStatus(limit.Path);
            var usedMin = (st?.UsedSeconds ?? 0) / 60;
            var remMin = (st?.RemainingSeconds ?? 0) / 60;
            var summary = $"{usedMin}m used of {st?.TotalAllowedSeconds / 60 ?? limit.DailyMinutes}m allowance • {remMin}m left today";
            var isBlocked = st?.IsExhausted == true;
            var statusText = !limit.Enabled ? "Disabled" : (isBlocked ? "Limit reached (Blocked)" : "Active");
            var statusColor = !limit.Enabled ? Brushes.Gray : (isBlocked ? Brushes.Firebrick : Brushes.SeaGreen);
            return new TimeLimitRow(limit, summary, statusText, statusColor, limit.Enabled ? "Disable" : "Enable");
        }).ToList();
    }

    private void SelectLimit(object sender, SelectionChangedEventArgs e)
    {
        if (LimitsList.SelectedItem is not TimeLimitRow row) return;
        LimitName.Text = row.Limit.Name; LimitPath.Text = row.Limit.Path;
        LimitMinutes.Text = row.Limit.DailyMinutes.ToString(); LimitEnabled.IsChecked = row.Limit.Enabled;
    }

    private void SaveLimit(object sender, RoutedEventArgs e)
    {
        if (state is null || !authenticate()) return;
        if (!int.TryParse(LimitMinutes.Text.Trim(), out var minutes)) { status("Enter a valid number of daily minutes (e.g. 60)."); return; }
        var limit = new AppTimeLimit(LimitPath.Text.Trim(), LimitName.Text.Trim(), minutes, LimitEnabled.IsChecked == true);
        if (AppTimeLimit.Validate(limit) is { } error) { status(error); return; }
        state.TimeLimits.RemoveAll(l => string.Equals(l.Path, limit.Path, StringComparison.OrdinalIgnoreCase));
        state.TimeLimits.Add(limit);
        if (save()) { status($"Daily limit of {minutes}m saved for {limit.Name}."); Refresh(); ClearForm(sender, e); }
    }

    private void ToggleLimit(object sender, RoutedEventArgs e)
    {
        if (state is null || !authenticate() || sender is not Button { Tag: string path }) return;
        var idx = state.TimeLimits.FindIndex(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        var cur = state.TimeLimits[idx];
        state.TimeLimits[idx] = cur with { Enabled = !cur.Enabled };
        save(); Refresh(); status($"{cur.Name} daily limit {(cur.Enabled ? "disabled" : "enabled")}.");
    }

    private void Add15Minutes(object sender, RoutedEventArgs e) => AddBonus(sender, 15);
    private void Add30Minutes(object sender, RoutedEventArgs e) => AddBonus(sender, 30);
    private void AddBonus(object sender, int minutes)
    {
        if (tracker is null || !authenticate() || sender is not Button { Tag: string path }) return;
        tracker.GrantBonusMinutes(path, minutes); save(); Refresh(); status($"Granted +{minutes}m bonus time.");
    }

    private void DeleteLimit(object sender, RoutedEventArgs e)
    {
        if (state is null || !authenticate() || sender is not Button { Tag: string path }) return;
        state.TimeLimits.RemoveAll(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase));
        save(); Refresh(); ClearForm(sender, e); status("Daily limit removed.");
    }

    private void BrowseExecutable(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        LimitPath.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(LimitName.Text)) LimitName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (string.IsNullOrWhiteSpace(LimitMinutes.Text)) LimitMinutes.Text = "60";
    }

    private void ClearForm(object sender, RoutedEventArgs e)
    {
        LimitName.Text = ""; LimitPath.Text = ""; LimitMinutes.Text = ""; LimitEnabled.IsChecked = true;
        LimitsList.SelectedItem = null; AppPicker.SelectedItem = null;
    }
}
