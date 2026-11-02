using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ITSResFix.Staff.Services;

/// <summary>One guarded key: how many misses, since when, and until when it is locked.</summary>
public record ThrottleEntry(int Failures, DateTime FirstFailure, DateTime? LockedUntil);

/// <summary>
/// Brute-force guard. A key locks for 15 minutes once it reaches its failure limit.
/// Callers use an account key (the cellphone or student id being targeted), never the IP alone, because many
/// students share one residence network address and must not be locked out by each other.
/// Decisions are made in memory (fast); <see cref="ThrottleStore"/> copies every change to the database and loads it back at
/// start-up, so a restart does not forget who was locked out. Keys are kept as keyed hashes, never as the raw phone, email or address.
/// </summary>
public class LoginThrottle
{
    private const int DefaultMaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, ThrottleEntry> _entries = new();
    private readonly byte[] _hashKey;

    /// <summary>Raised after a key changed: the hashed key and its new state (null = cleared).</summary>
    public event Action<string, ThrottleEntry?>? Changed;

    public LoginThrottle(IConfiguration config)
    {
        // the same long secret that signs student tokens; any value works as long as it stays the same between restarts
        _hashKey = Encoding.UTF8.GetBytes(config["Student:JwtKey"] ?? "resfix-throttle");
    }

    private string Hash(string key) => Convert.ToHexString(HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>Puts back what the database remembered (called once at start-up).</summary>
    public void Restore(string keyHash, ThrottleEntry entry) => _entries[keyHash] = entry;

    public bool IsLocked(string key, out TimeSpan remaining)
    {
        var h = Hash(key);
        remaining = TimeSpan.Zero;
        if (!_entries.TryGetValue(h, out var e) || e.LockedUntil is null) return false;
        if (e.LockedUntil <= DateTime.UtcNow) { if (_entries.TryRemove(h, out _)) Changed?.Invoke(h, null); return false; }
        remaining = e.LockedUntil.Value - DateTime.UtcNow;
        return true;
    }

    public void Fail(string key, int maxFailures = DefaultMaxFailures)
    {
        var h = Hash(key);
        var now = DateTime.UtcNow;
        var updated = _entries.AddOrUpdate(h,
            _ => new ThrottleEntry(1, now, null),
            (_, e) =>
            {
                if (now - e.FirstFailure > Window) return new ThrottleEntry(1, now, null);
                var n = e.Failures + 1;
                return new ThrottleEntry(n, e.FirstFailure, n >= maxFailures ? now + Window : null);
            });
        Changed?.Invoke(h, updated);
    }

    public void Reset(string key)
    {
        var h = Hash(key);
        if (_entries.TryRemove(h, out _)) Changed?.Invoke(h, null);
    }

    public static string Minutes(TimeSpan t) => Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)).ToString();
}
