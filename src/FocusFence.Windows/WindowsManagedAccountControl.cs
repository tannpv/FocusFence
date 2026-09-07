using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FocusFence.Core;

namespace FocusFence.Windows;

public sealed class WindowsManagedAccountControl(string scriptPath) : IManagedAccountControl
{
    public Task<ManagedPolicyOutcome> ReviewAndApply(ManagedAccountRequest request)
    {
        request.Validate();
        return Run("ReviewAndApply", request.AccountName, request.RestrictNewApps, request.BlockUninstallers, request.BlockedExecutables);
    }

    public Task<ManagedPolicyOutcome> Restore(string accountName)
    {
        new ManagedAccountRequest(accountName, true, false).Validate();
        return Run("Restore", accountName, false, false, []);
    }

    public static ManagedPolicyOutcome InterpretExitCode(int exitCode)
    {
        if (!Enum.IsDefined(typeof(ManagedPolicyOutcome), exitCode))
            throw new InvalidOperationException("The administrator helper did not confirm completion. Check its error dialog before retrying; policy may require recovery.");
        return (ManagedPolicyOutcome)exitCode;
    }

    private async Task<ManagedPolicyOutcome> Run(string action, string accountName, bool restrictNewApps, bool blockUninstallers, IReadOnlyList<string> blockedExecutables)
    {
        if (!File.Exists(scriptPath)) throw new FileNotFoundException("The administrator helper is missing. Keep the published app folder together.", scriptPath);
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(shell) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.GetFullPath(scriptPath), "-Action", action, "-AccountName", accountName })
            start.ArgumentList.Add(argument);
        if (restrictNewApps) start.ArgumentList.Add("-RestrictNewApps");
        if (blockUninstallers) start.ArgumentList.Add("-BlockUninstallers");
        if (blockedExecutables.Count > 0)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(blockedExecutables)));
            if (encoded.Length > 20_000) throw new ArgumentException("The selected app paths are too long for one policy request. Select fewer apps.");
            start.ArgumentList.Add("-BlockedExecutablesBase64"); start.ArgumentList.Add(encoded);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start the administrator helper.");
        await process.WaitForExitAsync();
        return InterpretExitCode(process.ExitCode);
    }
}
