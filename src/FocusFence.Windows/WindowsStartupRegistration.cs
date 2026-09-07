using FocusFence.Core;
using Microsoft.Win32;

namespace FocusFence.Windows;

/// <summary>Owns only the application's named entry in the current user's Run key.</summary>
public sealed class WindowsStartupRegistration(string executablePath) : IStartupRegistration
{
    public const string StartupArgument = "--startup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FocusFence";
    private const int MaximumCommandLength = 260;

    public static string BuildCommand(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.Contains('"'))
            throw new ArgumentException("Startup requires a full executable path.");
        var command = $"\"{Path.GetFullPath(path)}\" {StartupArgument}";
        if (command.Length > MaximumCommandLength) throw new ArgumentException("Move FocusFence to a shorter folder path before enabling startup.");
        return command;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var command = BuildCommand(executablePath);
            if (!File.Exists(executablePath)) throw new FileNotFoundException("The FocusFence executable could not be found.", executablePath);
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
