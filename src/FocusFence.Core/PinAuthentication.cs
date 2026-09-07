using System.Security.Cryptography;

namespace FocusFence.Core;

public sealed record PinProfile(string Salt, string Hash, int Iterations, int FailedAttempts = 0, DateTimeOffset? LockedUntil = null);
public sealed record PinResult(bool Success, string Message);

/// <summary>Local application authentication, independent of Windows administrator credentials.</summary>
public sealed class PinAuthentication(AppState state, IStateStore store, TimeProvider clock)
{
    public const int MinimumLength = 6;
    public const int MaximumLength = 12;
    public const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int AttemptLimit = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);
    private bool authenticated;
    public bool IsConfigured => state.Pin is not null;
    public bool IsUnlocked => !IsConfigured || authenticated;

    public static bool ValidFormat(string pin) => pin.Length is >= MinimumLength and <= MaximumLength && pin.All(c => c is >= '0' and <= '9');
    public static bool ValidProfile(PinProfile pin)
    {
        try { return pin.Iterations == Iterations && Convert.FromBase64String(pin.Salt).Length == SaltBytes && Convert.FromBase64String(pin.Hash).Length == HashBytes && pin.FailedAttempts is >= 0 and < AttemptLimit; }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException) { return false; }
    }
    public void Lock() => authenticated = false;

    public void SetPin(string pin)
    {
        if (!IsUnlocked) throw new InvalidOperationException("Unlock FocusFence before changing the PIN.");
        if (!ValidFormat(pin)) throw new ArgumentException($"Use {MinimumLength}–{MaximumLength} digits.");
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        try
        {
            Persist(new(Convert.ToBase64String(salt), Convert.ToBase64String(hash), Iterations));
            authenticated = true;
        }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }

    public PinResult Unlock(string pin)
    {
        if (state.Pin is not { } profile) return new(true, "No PIN configured.");
        var now = clock.GetUtcNow();
        if (profile.LockedUntil is { } until && until > now)
            return new(false, $"Try again in {Math.Ceiling((until - now).TotalSeconds)} seconds.");
        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(profile.Salt), profile.Iterations, HashAlgorithmName.SHA256, HashBytes);
        var matches = CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(profile.Hash));
        CryptographicOperations.ZeroMemory(actual);
        if (matches)
        {
            Persist(profile with { FailedAttempts = 0, LockedUntil = null });
            authenticated = true;
            return new(true, "Unlocked.");
        }
        var attempts = profile.FailedAttempts + 1;
        Persist(profile with { FailedAttempts = attempts >= AttemptLimit ? 0 : attempts, LockedUntil = attempts >= AttemptLimit ? now + LockDuration : null });
        return new(false, attempts >= AttemptLimit ? $"Too many attempts. Try again in {LockDuration.TotalSeconds} seconds." : "Incorrect PIN.");
    }

    private void Persist(PinProfile value)
    {
        var previous = state.Pin;
        state.Pin = value;
        try { store.Save(state); }
        catch { state.Pin = previous; authenticated = false; throw; }
    }
}
