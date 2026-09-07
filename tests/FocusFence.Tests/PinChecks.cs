using FocusFence.Core;

internal static class PinChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new AppState();
        var store = new TestStore(state);
        var clock = new PinClock();
        var auth = new PinAuthentication(state, store, clock);
        check(!PinAuthentication.ValidFormat("123") && !PinAuthentication.ValidFormat("abcdef") && PinAuthentication.ValidFormat("123456"), "PIN requires six to twelve digits");
        auth.SetPin("123456");
        var first = state.Pin!;
        check(first.Hash != "123456" && first.Salt.Length > 0 && PinAuthentication.ValidProfile(first), "PIN stored as a salted, validated hash");
        auth.SetPin("123456");
        check(first.Hash != state.Pin!.Hash && first.Salt != state.Pin.Salt, "Identical PINs receive independent random salts");
        auth.Lock();
        check(!auth.IsUnlocked && !auth.Unlock("111111").Success, "Locked controls reject an incorrect PIN");
        check(auth.Unlock("123456").Success && auth.IsUnlocked, "Correct PIN unlocks controls");
        auth.Lock();
        for (var attempt = 0; attempt < 5; attempt++) auth.Unlock("111111");
        check(!auth.Unlock("123456").Success, "Retry lockout also rejects the correct PIN until cooldown ends");
        auth = new(state, store, clock);
        check(!auth.Unlock("123456").Success, "Retry lockout survives authentication controller restart");
        clock.Advance(TimeSpan.FromMinutes(1));
        check(auth.Unlock("123456").Success && state.Pin!.FailedAttempts == 0, "PIN login works after cooldown and clears failed attempts");
        auth.SetPin("654321"); auth.Lock();
        check(!auth.Unlock("123456").Success && auth.Unlock("654321").Success, "PIN change invalidates the previous PIN");
        auth.Lock(); store.Fail = true;
        try { auth.Unlock("654321"); throw new Exception("Persistence failure was ignored."); }
        catch (IOException) { check(!auth.IsUnlocked, "Authentication fails closed when state cannot be saved"); }
        store.Fail = false;
        check(!PinAuthentication.ValidProfile(state.Pin! with { Iterations = 1 }), "Tampered hashing parameters rejected");
    }
    private sealed class PinClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }
    private sealed class TestStore(AppState state) : IStateStore
    {
        public bool Fail;
        public AppState Load() => state;
        public void Save(AppState value) { if (Fail) throw new IOException("Simulated storage failure."); }
    }
}
