using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusFence.Core;

namespace FocusFence.UiSmoke;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application();
        app.Resources.Source = new Uri("/FocusFence.App;component/Theme.xaml", UriKind.Relative);
        var store = new MemoryStore();
        var startup = new MemoryStartup();
        var accountControl = new MemoryAccountControl();
        var window = new FocusFence.App.MainWindow(store, new NoProcesses(), new AllowTargets(), startup, managedAccount: accountControl, catalog: new SampleCatalog());
        window.Show();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            await System.Threading.Tasks.Task.Delay(150);
            var pages = (TabControl)window.FindName("Pages");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
            for (var index = 0; index < pages.Items.Count; index++)
            {
                pages.SelectedIndex = index;
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = index == 0 ? args[0] : Path.ChangeExtension(args[0], $"tab{index}.png");
                using (var output = File.Create(path)) encoder.Save(output);
            }
            Console.WriteLine($"PASS: All {pages.Items.Count} WPF tabs loaded and rendered with isolated in-memory settings.");
            var appEditor = (FocusFence.App.AppAccessEditor)window.FindName("AppAccess");
            var appList = (ListBox)appEditor.FindName("AppList");
            if (appList.Items.Count != 2) throw new Exception("App discovery did not populate the app list.");
            pages.SelectedIndex = 0; window.UpdateLayout();
            var appButton = FindButton(appList, "Disable") ?? throw new Exception("App toggle was not rendered.");
            appButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.State.ManagedDisabledApps.Count != 1) throw new Exception("Individual app disable was not saved.");
            window.UpdateLayout();
            (FindButton(appList, "Enable") ?? throw new Exception("Enable control missing.")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.State.ManagedDisabledApps.Count != 0) throw new Exception("Individual app enable did not remove its draft restriction.");
            Console.WriteLine("PASS: Individual app disable/enable updates the saved draft.");
            var editor = (FocusFence.App.ScheduleEditor)window.FindName("Schedules");
            ((TextBox)editor.FindName("ScheduleName")).Text = "UI test routine";
            ((TextBox)editor.FindName("StartTime")).Text = "22:00";
            ((TextBox)editor.FindName("EndTime")).Text = "06:00";
            ((Button)editor.FindName("SaveScheduleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.State.Schedules.Count != 1 || store.State.Schedules[0].StartsAt != new TimeOnly(22, 0) || store.State.Schedules[0].Enabled)
                throw new Exception("Schedule editor did not persist the disabled overnight schedule.");
            ((TextBox)editor.FindName("StartTime")).Text = "not a time";
            ((Button)editor.FindName("SaveScheduleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.State.Schedules[0].StartsAt != new TimeOnly(22, 0)) throw new Exception("Invalid UI input changed the schedule.");
            Console.WriteLine("PASS: Schedule editor saves valid input and rejects invalid time input without process access.");
            var startupCheck = (CheckBox)window.FindName("StartupEnabled");
            var startupButton = (Button)window.FindName("SaveStartupButton");
            startupCheck.IsChecked = true;
            startupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!startup.Enabled) throw new Exception("Startup preference was not enabled.");
            startupCheck.IsChecked = false;
            startupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (startup.Enabled) throw new Exception("Startup preference was not disabled.");
            startup.FailWrites = true;
            startupCheck.IsChecked = true;
            startupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (startupCheck.IsChecked == true || !((TextBlock)window.FindName("Status")).Text.Contains("Could not change"))
                throw new Exception("Startup write failure was not reported and reverted in the UI.");
            Console.WriteLine("PASS: Startup settings enable, disable, and report write failures using fake registration.");
            var managedEditor = (FocusFence.App.ManagedAccountEditor)window.FindName("ManagedAccount");
            store.State.ManagedDisabledApps.Add(new("Sample game", @"C:\Apps\Game.exe"));
            ((TextBox)managedEditor.FindName("AccountName")).Text = "TestStandardUser";
            ((CheckBox)managedEditor.FindName("RestrictNewApps")).IsChecked = true;
            ((Button)managedEditor.FindName("SaveDraftButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.State.ManagedDraft != new ManagedPolicyDraft("TestStandardUser", true, false) || accountControl.Request is not null)
                throw new Exception("Saving the draft did not persist options independently of administrator policy application.");
            ((Button)appEditor.FindName("ReviewDraftButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (pages.SelectedItem != window.FindName("ManagedAccountTab")) throw new Exception("App list review did not navigate to the managed-account tab.");
            var reopenedEditor = new FocusFence.App.ManagedAccountEditor();
            reopenedEditor.Initialize(accountControl, store.State, () => true, () => true);
            if (((TextBox)reopenedEditor.FindName("AccountName")).Text != "TestStandardUser" || ((CheckBox)reopenedEditor.FindName("RestrictNewApps")).IsChecked != true)
                throw new Exception("Saved account options were not restored in the editor.");
            var failedControl = new MemoryAccountControl();
            reopenedEditor.Initialize(failedControl, store.State, () => true, () => false);
            ((TextBox)reopenedEditor.FindName("AccountName")).Text = "UnsavedAccount";
            ((Button)reopenedEditor.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.State.ManagedDraft.AccountName != "TestStandardUser" || failedControl.Request is not null)
                throw new Exception("A failed draft save changed state or launched the administrator helper.");
            Console.WriteLine("PASS: Account draft persists, reopens, navigates from the app list, and blocks administrator launch on save failure.");
            ((Button)managedEditor.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (accountControl.Request?.AccountName != "TestStandardUser" || accountControl.Request.RestrictNewApps != true || accountControl.Request.BlockedExecutables.Single() != @"C:\Apps\Game.exe")
                throw new Exception("Managed account request was not passed to the administrator service.");
            var accountResult = (TextBlock)managedEditor.FindName("Result");
            if (!accountResult.Text.Contains("Local policy applied")) throw new Exception("Applied result not shown.");
            accountControl.NextOutcome = ManagedPolicyOutcome.Cancelled;
            ((Button)managedEditor.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!accountResult.Text.Contains("Review cancelled")) throw new Exception("Cancelled review was not distinguished from apply.");
            ((Button)managedEditor.FindName("RestoreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (accountControl.RestoredAccount != "TestStandardUser") throw new Exception("Restore used the wrong account.");
            if (!accountResult.Text.Contains("Previous policy restored")) throw new Exception("Restored result not shown.");
            accountControl.RestoreOutcome = ManagedPolicyOutcome.NoPolicyToRestore;
            ((Button)managedEditor.FindName("RestoreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!accountResult.Text.Contains("No active FocusFence policy")) throw new Exception("No-policy result not shown.");
            accountControl.RestoreOutcome = ManagedPolicyOutcome.RestoredServiceRunning;
            ((Button)managedEditor.FindName("RestoreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!accountResult.Text.Contains("kept Application Identity running")) throw new Exception("Service restore caveat not shown.");
            Console.WriteLine("PASS: Administrator outcomes distinguish apply, cancel, restore, no policy, and a still-running service.");
            accountControl.Fail = true;
            ((Button)managedEditor.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!((TextBlock)managedEditor.FindName("Result")).Text.Contains("Test failure")) throw new Exception("Managed account failure was not shown.");
            Console.WriteLine("PASS: Managed account controls forward account/options, restore, and report helper errors without elevation.");
            var backgroundStore = new MemoryStore();
            backgroundStore.State.Session = new(DateTimeOffset.UtcNow.AddMinutes(1), [new("Fake", @"C:\Fake\app.exe")]);
            var backgroundMonitor = new NoProcesses();
            var hidden = new FocusFence.App.MainWindow(backgroundStore, backgroundMonitor, new AllowTargets(), new MemoryStartup(), background: true);
            hidden.Show();
            await System.Threading.Tasks.Task.Delay(150);
            if (hidden.IsVisible || backgroundMonitor.BlockCalls == 0) throw new Exception("Background launch did not hide and resume monitoring.");
            hidden.Show();
            if (!hidden.IsVisible) throw new Exception("Background window could not reopen.");
            Console.WriteLine("PASS: Background launch resumes fake blocking, stays hidden, and reopens correctly.");
            var protectedStore = new MemoryStore();
            new PinAuthentication(protectedStore.State, protectedStore, TimeProvider.System).SetPin("123456");
            var protectedWindow = new FocusFence.App.MainWindow(protectedStore, new NoProcesses(), new AllowTargets());
            protectedWindow.Show();
            if (((TabControl)protectedWindow.FindName("Pages")).IsEnabled) throw new Exception("Configured PIN did not lock the window on startup.");
            _ = protectedWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = app.Windows.OfType<FocusFence.App.PinDialog>().Single();
                dialog.UpdateLayout();
                var loginImage = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                loginImage.Render(dialog);
                var loginEncoder = new PngBitmapEncoder(); loginEncoder.Frames.Add(BitmapFrame.Create(loginImage));
                using (var output = File.Create(Path.ChangeExtension(args[0], "login.png"))) loginEncoder.Save(output);
                ((PasswordBox)dialog.FindName("Pin")).Password = "123456";
                ((Button)dialog.FindName("Submit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            ((Button)protectedWindow.FindName("SignInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!((TabControl)protectedWindow.FindName("Pages")).IsEnabled) throw new Exception("PIN dialog did not unlock the UI.");
            protectedWindow.Close();
            if (((TabControl)protectedWindow.FindName("Pages")).IsEnabled) throw new Exception("Closing to tray did not lock controls.");
            Console.WriteLine("PASS: PIN login unlocks controls and closing to tray locks them again.");
            app.Shutdown();
        }));
        app.Run();
    }
    private static Button? FindButton(DependencyObject root, string content)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button && button.Content?.ToString() == content) return button;
            if (FindButton(child, content) is { } found) return found;
        }
        return null;
    }
    private sealed class SampleCatalog : IAppCatalog
    {
        public IReadOnlyList<CatalogApp> Discover() => [new("Sample browser", @"C:\Apps\Browser.exe", "Test catalog", null), new("Sample game", @"C:\Apps\Game.exe", "Test catalog", null)];
    }
    private sealed class MemoryStore : IStateStore
    {
        public AppState State { get; } = new();
        public AppState Load() => State;
        public void Save(AppState state) { }
    }
    private sealed class NoProcesses : IAppMonitor
    {
        public int BlockCalls;
        public IReadOnlyList<AppTarget> GetRunningApps() => [];
        public IReadOnlyList<string> Block(IReadOnlyList<AppTarget> targets, DateTimeOffset endsAt) { System.Threading.Interlocked.Increment(ref BlockCalls); return []; }
    }
    private sealed class MemoryStartup : IStartupRegistration
    {
        public bool Enabled;
        public bool FailWrites;
        public bool IsEnabled() => Enabled;
        public void SetEnabled(bool enabled)
        {
            if (FailWrites) throw new UnauthorizedAccessException("Test access denied.");
            Enabled = enabled;
        }
    }
    private sealed class AllowTargets : ITargetPolicy { public string? GetRejection(string path) => null; }
    private sealed class MemoryAccountControl : IManagedAccountControl
    {
        public ManagedAccountRequest? Request;
        public string? RestoredAccount;
        public bool Fail;
        public ManagedPolicyOutcome NextOutcome = ManagedPolicyOutcome.Applied;
        public ManagedPolicyOutcome RestoreOutcome = ManagedPolicyOutcome.Restored;
        public System.Threading.Tasks.Task<ManagedPolicyOutcome> ReviewAndApply(ManagedAccountRequest request)
        {
            if (Fail) throw new InvalidOperationException("Test failure from helper.");
            Request = request; return System.Threading.Tasks.Task.FromResult(NextOutcome);
        }
        public System.Threading.Tasks.Task<ManagedPolicyOutcome> Restore(string accountName)
        { RestoredAccount = accountName; return System.Threading.Tasks.Task.FromResult(RestoreOutcome); }
    }
}
