using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FocusFence.Core;

namespace FocusFence.App;

public partial class MainWindow
{
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
}
