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
    private List<AppTarget> DisabledApps { get => state!.UseCurrentAccount ? state.LocalDisabledApps : state.ManagedDisabledApps; set { if (state!.UseCurrentAccount) state.LocalDisabledApps = value; else state.ManagedDisabledApps = value; } }
    private void ChangeAccountMode(object sender, RoutedEventArgs e)
    {
        if (state is null || !authorize()) return;
        var previous = state.UseCurrentAccount;
        state.UseCurrentAccount = CurrentAccount.IsChecked == true;
        if (!save()) state.UseCurrentAccount = previous;
        CurrentAccount.IsChecked = state.UseCurrentAccount;
        Render();
    }
    public event EventHandler? ReviewRequested;
    public void RefreshSummary()
    {
        if (state is null) return;
        var account = string.IsNullOrWhiteSpace(state.ManagedDraft.AccountName) ? "Choose an account" : state.ManagedDraft.AccountName;
        DraftSummary.Text = state.UseCurrentAccount ? $"Current account · {DisabledApps.Count} apps monitored while FocusFence runs" : $"{account} · {DisabledApps.Count} disabled in draft";
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
    { this.state = state; this.catalog = catalog; this.policy = policy; this.save = save; this.authorize = authorize; CurrentAccount.IsChecked = state.UseCurrentAccount; Render(); }
    private async void RefreshApps(object sender, RoutedEventArgs e)
    {
        if (catalog is null || !authorize()) return;
        try { apps = (await Task.Run(catalog.Discover)).ToList(); Render(); Result.Text = $"Found {apps.Count} desktop executables. Current-account switches apply while FocusFence runs; managed-account changes require review."; }
        catch (Exception ex) { Result.Text = "Could not discover apps: " + ex.Message; }
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (AppList is not null) Render(); }
    private void Render()
    {
        if (state is null) return;
        RefreshSummary();
        var rows = apps.Concat(DisabledApps.Select(a => new CatalogApp(a.Name, a.Path, "Saved selection", policy?.GetRejection(a.Path))))
            .DistinctBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .Where(a => a.Name.Contains(Search.Text, StringComparison.OrdinalIgnoreCase) || a.Path.Contains(Search.Text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name).Select(a => new AppRow(a, DisabledApps.Any(d => string.Equals(d.Path, a.Path, StringComparison.OrdinalIgnoreCase)))).ToList();
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
        var previous = DisabledApps.ToList();
        DisabledApps.RemoveAll(a => string.Equals(a.Path, row.Path, StringComparison.OrdinalIgnoreCase));
        if (!row.Disabled) DisabledApps.Add(new(row.Name, row.Path));
        if (!save()) { DisabledApps = previous; Result.Text = "Could not save this change."; }
        else Result.Text = state.UseCurrentAccount ? "Saved. Disabled apps will be closed while FocusFence runs. Save work before disabling an app." : "Draft saved. Use Managed account → Review and apply to update Windows. To remove all restrictions, use Restore previous policy.";
        Render();
    }
    public sealed record AppRow(CatalogApp App, bool Disabled)
    {
        public string Name => App.Name;
        public string Path => App.Path;
        public bool CanChange => App.Restriction is null || Disabled;
        public string ActionLabel => Disabled ? "Enable" : "Disable";
        public string Status => App.Restriction ?? (Disabled ? "Selected for blocking" : "Not selected for blocking") + " · " + App.Source;
    }
}
