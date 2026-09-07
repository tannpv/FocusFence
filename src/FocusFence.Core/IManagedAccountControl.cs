namespace FocusFence.Core;

public sealed record ManagedAccountRequest(string AccountName, bool RestrictNewApps, bool BlockUninstallers)
{
    public IReadOnlyList<string> BlockedExecutables { get; init; } = [];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountName) || AccountName.IndexOfAny(['\\', '/', '"', '\r', '\n']) >= 0)
            throw new ArgumentException("Enter a local Windows username, without a computer or domain prefix.");
        if (!RestrictNewApps && !BlockUninstallers && BlockedExecutables.Count == 0)
            throw new ArgumentException("Select a restriction or disable an app, or use Restore to remove the managed policy.");
        if (BlockedExecutables.Count > 100 || BlockedExecutables.Any(p => !Path.IsPathFullyQualified(p) || !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose up to 100 executables using their full paths.");
    }
}

public interface IManagedAccountControl
{
    Task<ManagedPolicyOutcome> ReviewAndApply(ManagedAccountRequest request);
    Task<ManagedPolicyOutcome> Restore(string accountName);
}

// Explicit helper protocol codes. Ordinary exit 0 is intentionally not an applied result.
public enum ManagedPolicyOutcome
{
    Applied = 10,
    Restored = 11,
    Cancelled = 12,
    NoPolicyToRestore = 13,
    RestoredServiceRunning = 14
}
