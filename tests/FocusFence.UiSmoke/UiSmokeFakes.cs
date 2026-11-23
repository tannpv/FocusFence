using FocusFence.Core;

namespace FocusFence.UiSmoke;

internal sealed class SampleCatalog : IAppCatalog
{
    public IReadOnlyList<CatalogApp> Discover() => [new("Sample browser", @"C:\Apps\Browser.exe", "Test catalog", null), new("Sample game", @"C:\Apps\Game.exe", "Test catalog", null)];
}

internal sealed class MemoryStore : IStateStore
{
    public AppState State { get; } = new();
    public AppState Load() => State;
    public void Save(AppState state) { }
}

internal sealed class NoProcesses : IAppMonitor
{
    public int BlockCalls;
    public IReadOnlyList<AppTarget> GetRunningApps() => [];
    public IReadOnlyList<string> Block(IReadOnlyList<AppTarget> targets, DateTimeOffset endsAt) { System.Threading.Interlocked.Increment(ref BlockCalls); return []; }
}

internal sealed class MemoryStartup : IStartupRegistration
{
    public bool Enabled;
    public bool FailWrites;
    public bool IsEnabled() => Enabled;
    public void SetEnabled(bool enabled)
    {
        if (FailWrites) throw new UnauthorizedAccessException("Test access denied.");
        Enabled = enabled;
    }
}

internal sealed class AllowTargets : ITargetPolicy { public string? GetRejection(string path) => null; }

internal sealed class MemoryAccountControl : IManagedAccountControl
{
    public ManagedAccountRequest? Request;
    public string? RestoredAccount;
    public bool Fail;
    public ManagedPolicyOutcome NextOutcome = ManagedPolicyOutcome.Applied;
    public ManagedPolicyOutcome RestoreOutcome = ManagedPolicyOutcome.Restored;
    public System.Threading.Tasks.Task<ManagedPolicyOutcome> ReviewAndApply(ManagedAccountRequest request)
    {
        if (Fail) throw new InvalidOperationException("Test failure from helper.");
        Request = request; return System.Threading.Tasks.Task.FromResult(NextOutcome);
    }
    public System.Threading.Tasks.Task<ManagedPolicyOutcome> Restore(string accountName)
    { RestoredAccount = accountName; return System.Threading.Tasks.Task.FromResult(RestoreOutcome); }
}
