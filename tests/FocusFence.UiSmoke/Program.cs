using System.Windows;
using System.Windows.Controls;
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
        var store = new MemoryStore(); var startup = new MemoryStartup(); var accountControl = new MemoryAccountControl();
        var window = new FocusFence.App.MainWindow(store, new NoProcesses(), new AllowTargets(), startup, managedAccount: accountControl, catalog: new SampleCatalog());
        window.Show();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            await System.Threading.Tasks.Task.Delay(150);
            var pages = (TabControl)window.FindName("Pages");
            UiSmokeHelpers.RenderTabs(pages, window, args[0]);
            Console.WriteLine($"PASS: All {pages.Items.Count} WPF tabs loaded and rendered with isolated in-memory settings.");
            var appEditor = (FocusFence.App.AppAccessEditor)window.FindName("AppAccess");
            var appList = (ListBox)appEditor.FindName("AppList");
            if (appList.Items.Count != 2) throw new Exception("App discovery did not populate the app list.");
            pages.SelectedIndex = 0; window.UpdateLayout();
            var appButton = UiSmokeHelpers.FindButton(appList, "Disable") ?? throw new Exception("App toggle was not rendered.");
            appButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.State.ManagedDisabledApps.Count != 1) throw new Exception("Individual app disable was not saved.");
            window.UpdateLayout();
            (UiSmokeHelpers.FindButton(appList, "Enable") ?? throw new Exception("Enable control missing.")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            var limitEditor = (FocusFence.App.TimeLimitEditor)window.FindName("TimeLimits");
            ((TextBox)limitEditor.FindName("LimitName")).Text = "Roblox";
            ((TextBox)limitEditor.FindName("LimitPath")).Text = @"C:\Games\roblox.exe";
            ((TextBox)limitEditor.FindName("LimitMinutes")).Text = "60";
            ((Button)limitEditor.FindName("SaveLimitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.State.TimeLimits.Count != 1 || store.State.TimeLimits[0].DailyMinutes != 60)
                throw new Exception("Time limit editor did not persist the daily limit.");
            var appPicker = (ComboBox)limitEditor.FindName("AppPicker");
            if (appPicker.Items.Count > 0)
            {
                appPicker.SelectedIndex = 0;
                if (string.IsNullOrWhiteSpace(((TextBox)limitEditor.FindName("LimitName")).Text))
                    throw new Exception("App picker selection did not automatically populate the app limit name.");
            }
            Console.WriteLine("PASS: Time limit editor configures and persists per-app daily screen time limits with app select list.");
            var startupCheck = (CheckBox)window.FindName("StartupEnabled");
            var startupButton = (Button)window.FindName("SaveStartupButton");
            startupCheck.IsChecked = true; startupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!startup.Enabled) throw new Exception("Startup preference was not enabled.");
            startupCheck.IsChecked = false; startupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (startup.Enabled) throw new Exception("Startup preference was not disabled.");
            startup.FailWrites = true; startupCheck.IsChecked = true; startupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (startupCheck.IsChecked == true || !((TextBlock)window.FindName("Status")).Text.Contains("Could not change"))
                throw new Exception("Startup write failure was not reported and reverted in the UI.");
            Console.WriteLine("PASS: Startup settings enable, disable, and report write failures using fake registration.");
            var managedEditor = (FocusFence.App.ManagedAccountEditor)window.FindName("ManagedAccount");
            UiSmokeHelpers.VerifyManagedAccount(window, pages, appEditor, managedEditor, store, accountControl);
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
            var protectedStore = new MemoryStore(); var localStore = new MemoryStore();
            localStore.State.LocalDisabledApps.Add(new("Local test", @"C:\Fake\local.exe"));
            var localMonitor = new NoProcesses();
            var localWindow = new FocusFence.App.MainWindow(localStore, localMonitor, new AllowTargets());
            localWindow.Show();
            await System.Threading.Tasks.Task.Delay(150);
            if (localMonitor.BlockCalls == 0 || localStore.State.Session is not null) throw new Exception("Current-account blocking did not run without a focus session.");
            localStore.State.LocalDisabledApps.Clear();
            await System.Threading.Tasks.Task.Delay(150);
            var previousCalls = localMonitor.BlockCalls;
            await System.Threading.Tasks.Task.Delay(1200);
            if (localMonitor.BlockCalls != previousCalls) throw new Exception("Enabling the local app did not stop blocking.");
            Console.WriteLine("PASS: Current-account app blocking works without a session and stops when enabled.");
            new PinAuthentication(protectedStore.State, protectedStore, TimeProvider.System).SetPin("123456");
            var protectedWindow = new FocusFence.App.MainWindow(protectedStore, new NoProcesses(), new AllowTargets());
            protectedWindow.Show();
            if (((TabControl)protectedWindow.FindName("Pages")).IsEnabled) throw new Exception("Configured PIN did not lock the window on startup.");
            UiSmokeHelpers.VerifyPinLogin(app, protectedWindow, args[0]);
            Console.WriteLine("PASS: PIN login unlocks controls and closing to tray locks them again.");
            app.Shutdown();
        }));
        app.Run();
    }
}
