using FocusFence.Core;

namespace FocusFence.Tests;

internal sealed class DirectForeground(string path) : IForegroundTracker
{
    public string? GetActiveExecutablePath() => path;
}

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
