using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FocusFence.Core;

namespace FocusFence.Windows;

public sealed class WindowsForegroundTracker : IForegroundTracker
{
    private readonly int currentSessionId = Process.GetCurrentProcess().SessionId;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public string? GetActiveExecutablePath()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId) return null;
        if (!ProcessIdToSessionId(processId, out var sessionId) || sessionId != (uint)currentSessionId) return null;

        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(hProcess, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }
}
