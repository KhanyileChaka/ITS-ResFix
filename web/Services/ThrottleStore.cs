using System.Collections.Concurrent;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

[Table("login_throttle")]
public class LoginThrottleRow : BaseModel
{
    [PrimaryKey("key_hash", true)]
    public string KeyHash { get; set; } = string.Empty;

    [Column("failures")]
    public int Failures { get; set; }

    [Column("first_failure")]
    public DateTime FirstFailure { get; set; }

    [Column("locked_until")]
    public DateTime? LockedUntil { get; set; }

    [Column("updated_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Remembers the sign-in lockout counters across restarts: loads them when the site starts, writes every change to the database
/// a couple of seconds later (so requests never wait for it), and writes the last changes again as the site shuts down.
/// If the table is missing or the database is unreachable the guard keeps working in memory and a warning is logged.
/// </summary>
public class ThrottleStore : BackgroundService
{
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly LoginThrottle _throttle;
    private readonly Supabase.Client _db;
    private readonly ILogger<ThrottleStore> _log;
    private readonly ConcurrentDictionary<string, ThrottleEntry?> _pending = new();

    public ThrottleStore(LoginThrottle throttle, Supabase.Client db, ILogger<ThrottleStore> log)
    {
        _throttle = throttle; _db = db; _log = log;
        _throttle.Changed += (key, entry) => _pending[key] = entry;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await LoadAsync();
        var lastClean = DateTime.UtcNow;
        while (!stop.IsCancellationRequested)
        {
            try { await Task.Delay(FlushEvery, stop); } catch (OperationCanceledException) { break; }
            await FlushAsync();
            if (DateTime.UtcNow - lastClean > TimeSpan.FromHours(6)) { lastClean = DateTime.UtcNow; await CleanAsync(); }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(); // the last few seconds of changes, written as the site shuts down
    }

    private async Task LoadAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow.Subtract(Window).ToString("o");
            var rows = (await _db.From<LoginThrottleRow>().Filter("first_failure", Operator.GreaterThan, cutoff).Get()).Models;
            foreach (var r in rows)
                _throttle.Restore(r.KeyHash, new ThrottleEntry(r.Failures, DateTime.SpecifyKind(r.FirstFailure, DateTimeKind.Utc),
                    r.LockedUntil is null ? null : DateTime.SpecifyKind(r.LockedUntil.Value, DateTimeKind.Utc)));
            _log.LogInformation("Sign-in guard: restored {Count} counters from the database", rows.Count);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Sign-in guard: could not load saved counters (is migration v21 applied?). Continuing in memory only.");
        }
    }

    private async Task FlushAsync()
    {
        if (_pending.IsEmpty) return;
        var batch = new List<KeyValuePair<string, ThrottleEntry?>>();
        foreach (var key in _pending.Keys.ToList())
            if (_pending.TryRemove(key, out var entry)) batch.Add(new(key, entry));
        try
        {
            var saves = batch.Where(b => b.Value is not null).Select(b => new LoginThrottleRow
            {
                KeyHash = b.Key, Failures = b.Value!.Failures, FirstFailure = b.Value.FirstFailure, LockedUntil = b.Value.LockedUntil
            }).ToList();
            if (saves.Count > 0) await _db.From<LoginThrottleRow>().Upsert(saves);
            var gone = batch.Where(b => b.Value is null).Select(b => (object)b.Key).ToList();
            if (gone.Count > 0) await _db.From<LoginThrottleRow>().Filter("key_hash", Operator.In, gone).Delete();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Sign-in guard: could not save {Count} counter changes", batch.Count);
        }
    }

    /// <summary>Old rows (no lock left, last touched long ago) are only clutter.</summary>
    private async Task CleanAsync()
    {
        try
        {
            // a live counter started at most half an hour ago (15 minute window plus a 15 minute lock)
            var old = DateTime.UtcNow.AddHours(-2).ToString("o");
            await _db.From<LoginThrottleRow>().Filter("first_failure", Operator.LessThan, old).Delete();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Sign-in guard: clean-up failed"); }
    }
}
