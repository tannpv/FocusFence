using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FocusFence.Core;

namespace FocusFence.App;

public partial class AppAccessEditor : UserControl
{
    private AppState? state;
    private IAppCatalog? catalog;
    private ITargetPolicy? policy;
    private Func<bool> save = () => false;
    private Func<bool> authorize = () => false;
    private List<CatalogApp> apps = [];
    private bool loaded;
    public event EventHandler? ReviewRequested;
    public void RefreshSummary()
    {
        if (state is null) return;
        var account = string.IsNullOrWhiteSpace(state.ManagedDraft.AccountName) ? "Choose an account" : state.ManagedDraft.AccountName;
        DraftSummary.Text = $"{account} · {state.ManagedDisabledApps.Count} disabled in draft";
    }
    private void ReviewDraft(object sender, RoutedEventArgs e)
    {
        if (authorize()) ReviewRequested?.Invoke(this, EventArgs.Empty);
    }
    public AppAccessEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (!loaded && catalog is not null && IsEnabled) { loaded = true; RefreshApps(this, new RoutedEventArgs()); } };
        IsEnabledChanged += (_, _) => { if (IsLoaded && !loaded && catalog is not null && IsEnabled) { loaded = true; RefreshApps(this, new RoutedEventArgs()); } };
    }
    public void Initialize(AppState state, IAppCatalog? catalog, ITargetPolicy policy, Func<bool> save, Func<bool> authorize)
    { this.state = state; this.catalog = catalog; this.policy = policy; this.save = save; this.authorize = authorize; Render(); }
    private async void RefreshApps(object sender, RoutedEventArgs e)
    {
        if (catalog is null || !authorize()) return;
        try { apps = (await Task.Run(catalog.Discover)).ToList(); Render(); Result.Text = $"Found {apps.Count} desktop executables. Changes need administrator review in Managed account."; }
        catch (Exception ex) { Result.Text = "Could not discover apps: " + ex.Message; }
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (AppList is not null) Render(); }
    private void Render()
    {
        if (state is null) return;
        RefreshSummary();
        var rows = apps.Concat(state.ManagedDisabledApps.Select(a => new CatalogApp(a.Name, a.Path, "Saved selection", policy?.GetRejection(a.Path))))
            .DistinctBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .Where(a => a.Name.Contains(Search.Text, StringComparison.OrdinalIgnoreCase) || a.Path.Contains(Search.Text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name).Select(a => new AppRow(a, state.ManagedDisabledApps.Any(d => string.Equals(d.Path, a.Path, StringComparison.OrdinalIgnoreCase)))).ToList();
        AppList.ItemsSource = rows;
    }
    private void BrowseApp(object sender, RoutedEventArgs e)
    {
        if (!authorize()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() == true)
        {
            try { apps.Add(new(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName, "Selected executable", policy?.GetRejection(dialog.FileName))); Render(); }
            catch (Exception ex) { Result.Text = ex.Message; }
        }
    }
    private void ToggleApp(object sender, RoutedEventArgs e)
    {
        if (state is null || !authorize() || ((Button)sender).DataContext is not AppRow row || !row.CanChange) return;
        var previous = state.ManagedDisabledApps.ToList();
        state.ManagedDisabledApps.RemoveAll(a => string.Equals(a.Path, row.Path, StringComparison.OrdinalIgnoreCase));
        if (!row.Disabled) state.ManagedDisabledApps.Add(new(row.Name, row.Path));
        if (!save()) { state.ManagedDisabledApps = previous; Result.Text = "Could not save this change."; }
        else Result.Text = "Draft saved. Use Managed account → Review and apply to update Windows. To remove all restrictions, use Restore previous policy.";
        Render();
    }
    public sealed record AppRow(CatalogApp App, bool Disabled)
    {
        public string Name => App.Name;
        public string Path => App.Path;
        public bool CanChange => App.Restriction is null || Disabled;
        public string ActionLabel => Disabled ? "Enable" : "Disable";
        public string Status => App.Restriction ?? (Disabled ? "Disabled in draft" : "Enabled in draft") + " · " + App.Source;
    }
}
