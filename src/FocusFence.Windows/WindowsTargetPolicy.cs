using FocusFence.Core;

namespace FocusFence.Windows;

public sealed class WindowsTargetPolicy : ITargetPolicy
{
    public string? GetRejection(string path)
    {
        if (!System.IO.Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "Select an executable using its full path.";
        var full = System.IO.Path.GetFullPath(path);
        if (string.Equals(full, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return "The blocker cannot block itself.";
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        if (full.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return "Windows system applications are protected.";
        // Reparse points can disguise a protected executable behind an apparently safe path.
        for (var current = full; !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return "Choose the original executable, not a symbolic link or redirected folder.";
        }
        return null;
    }
}
