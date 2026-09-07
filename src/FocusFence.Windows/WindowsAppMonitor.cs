using System.ComponentModel;
using System.Diagnostics;
using FocusFence.Core;

namespace FocusFence.Windows;

public sealed class WindowsAppMonitor(ITargetPolicy policy) : IAppMonitor
{
    private readonly int sessionId = Process.GetCurrentProcess().SessionId;
    public IReadOnlyList<AppTarget> GetRunningApps()
    {
        var apps = new List<AppTarget>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != sessionId || process.MainWindowHandle == IntPtr.Zero) continue;
                    var path = process.MainModule?.FileName;
                    if (path is not null && policy.GetRejection(path) is null)
                        apps.Add(new(process.ProcessName, path));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return apps.DistinctBy(a => a.Path, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name).ToList();
    }

    public IReadOnlyList<string> Block(IReadOnlyList<AppTarget> targets, DateTimeOffset endsAt)
    {
        var errors = new HashSet<string>();
        var allowed = targets.Where(t => policy.GetRejection(t.Path) is null).ToList();
        var names = allowed.Select(t => System.IO.Path.GetFileNameWithoutExtension(t.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string name = "Selected app";
                try
                {
                    name = process.ProcessName;
                    if (!names.Contains(name) || process.Id == Environment.ProcessId || process.SessionId != sessionId) continue;
                    var path = process.MainModule?.FileName;
                    if (DateTimeOffset.UtcNow < endsAt && path is not null && allowed.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)))
                        process.Kill(); // Exact executable only; never terminate unrelated child processes.
                }
                catch (Win32Exception) { if (names.Contains(name)) errors.Add($"Cannot inspect or close {name}: Windows denied access."); }
                catch (InvalidOperationException) { /* Process exited between enumeration and inspection. */ }
            }
        }
        return errors.ToList();
    }
}
