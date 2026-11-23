using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FocusFence.Core;

namespace FocusFence.Windows;

public sealed class WindowsForegroundTracker : IForegroundTracker
{
    private readonly int currentSessionId = Process.GetCurrentProcess().SessionId;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public string? GetActiveExecutablePath()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (process.SessionId != currentSessionId) return null;
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
