using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FocusFence.Core;

namespace FocusFence.Windows;

public sealed class WindowsAppMonitor(ITargetPolicy policy) : IAppMonitor
{
    private readonly int sessionId = Process.GetCurrentProcess().SessionId;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

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
                    var path = GetExecutablePath(process);
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
                    var path = GetExecutablePath(process);
                    if (DateTimeOffset.UtcNow < endsAt && path is not null && allowed.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)))
                        process.Kill();
                }
                catch (Win32Exception) { if (names.Contains(name)) errors.Add($"Cannot inspect or close {name}: Windows denied access."); }
                catch (InvalidOperationException) { /* Process exited between enumeration and inspection. */ }
            }
        }
        return errors.ToList();
    }

    private static string? GetExecutablePath(Process process)
    {
        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)process.Id);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                var size = buffer.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                    return buffer.ToString();
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }
        try { return process.MainModule?.FileName; }
        catch (Win32Exception) { return null; }
    }
}
