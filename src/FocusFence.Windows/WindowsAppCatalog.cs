using System.Runtime.InteropServices;
using FocusFence.Core;
using Microsoft.Win32;

namespace FocusFence.Windows;

/// <summary>Discovers desktop executable entry points without launching applications.</summary>
public sealed class WindowsAppCatalog(IAppMonitor monitor, ITargetPolicy policy) : IAppCatalog
{
    public IReadOnlyList<CatalogApp> Discover()
    {
        var apps = new List<CatalogApp>();
        void Add(string name, string? path, string source)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
            if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
            path = Path.GetFullPath(path);
            apps.Add(new(name, path, source, policy.GetRejection(path)));
        }
        foreach (var app in monitor.GetRunningApps()) Add(app.Name, app.Path, "Running app");
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var registrations = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
            if (registrations is null) continue;
            foreach (var name in registrations.GetSubKeyNames())
            {
                using var registration = registrations.OpenSubKey(name);
                Add(Path.GetFileNameWithoutExtension(name), registration?.GetValue(null) as string, "Registered app");
            }
        }
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is not null)
        {
            var shell = Activator.CreateInstance(shellType)!;
            try
            {
                foreach (var folder in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
                {
                    var directory = Environment.GetFolderPath(folder);
                    if (!Directory.Exists(directory)) continue;
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                    foreach (var link in Directory.EnumerateFiles(directory, "*.lnk", options))
                    {
                        object? shortcut = null;
                        try
                        {
                            shortcut = ((dynamic)shell).CreateShortcut(link);
                            Add(Path.GetFileNameWithoutExtension(link), (string)((dynamic)shortcut).TargetPath, "Start menu shortcut");
                        }
                        catch (COMException) { /* Broken shortcuts do not prevent other apps being listed. */ }
                        finally { if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut); }
                    }
                }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
        return apps.DistinctBy(a => a.Path, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name).ToList();
    }
}
