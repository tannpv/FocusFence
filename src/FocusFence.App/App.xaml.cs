using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using FocusFence.Windows;

namespace FocusFence.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\FocusFence.Desktop", out var created);
        if (!created) { MessageBox.Show("FocusFence is already running. Open it from the system tray."); Shutdown(); return; }
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusFence", "settings.json");
        try
        {
            var store = new JsonStateStore(path);
            var policy = new WindowsTargetPolicy();
            var startup = new WindowsStartupRegistration(Path.Combine(AppContext.BaseDirectory, "FocusFence.App.exe"));
            var background = e.Args.Contains(WindowsStartupRegistration.StartupArgument, StringComparer.OrdinalIgnoreCase) && startup.IsEnabled();
            var managedAccount = new WindowsManagedAccountControl(Path.Combine(AppContext.BaseDirectory, "Admin", "ManageAccount.ps1"));
            var monitor = new WindowsAppMonitor(policy);
            MainWindow = new MainWindow(store, monitor, policy, startup, background, managedAccount, new WindowsAppCatalog(monitor, policy));
            if (background) { MainWindow.Opacity = 0; MainWindow.ShowInTaskbar = false; MainWindow.ShowActivated = false; }
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"FocusFence could not start. Your settings were left intact.\n{path}\n\n{ex.Message}", "Startup error");
            Shutdown();
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
