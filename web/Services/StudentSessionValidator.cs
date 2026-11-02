using System.Security.Claims;
using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models.Db;
using Microsoft.Extensions.Caching.Memory;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Checks, on every student API request, that the token was issued after the account was last cancelled.
/// A manager who cancels a sign-up (wrong email or number) therefore ends the sessions and the half-finished
/// First Login of whoever used it. The cancel time is a marker row in password_reset (created_at is the time).
/// </summary>
public class StudentSessionValidator
{
    private record Snapshot(bool Exists, bool IsRegistered, long RevokedAtUnix);

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    public const string RevokedPrefix = "revoked:";

    private readonly Supabase.Client _db;
    private readonly IMemoryCache _cache;

    public StudentSessionValidator(Supabase.Client db, IMemoryCache cache) { _db = db; _cache = cache; }

    public void Invalidate(int userId) => _cache.Remove(Key(userId));

    /// <summary>A password-reset token works once: its id is remembered until the token would have expired anyway.</summary>
    public void MarkUsed(string? tokenId)
    {
        if (!string.IsNullOrEmpty(tokenId)) _cache.Set($"used-token|{tokenId}", true, TimeSpan.FromMinutes(15));
    }

    public async Task<bool> IsValidAsync(ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue("jti") is { } jti && _cache.TryGetValue($"used-token|{jti}", out _)) return false;
        if (!int.TryParse(principal.FindFirstValue("studentId"), out var studentId)) return false;
        if (!int.TryParse(principal.FindFirstValue("sub"), out var userId)) return false;
        if (!long.TryParse(principal.FindFirstValue("nbf"), out var issuedAt)) return false;

        if (!_cache.TryGetValue(Key(userId), out Snapshot? snap) || snap is null)
        {
            var student = (await _db.From<Student>().Filter("student_id", Operator.Equals, studentId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
            var marker = (await _db.From<PasswordResetRow>()
                .Filter("user_id", Operator.Equals, userId.ToString())
                .Filter("token_hash", Operator.ILike, RevokedPrefix + "%")
                .Order("created_at", Ordering.Descending).Limit(1).Get()).Models.FirstOrDefault();
            var at = marker is null ? 0 : new DateTimeOffset(marker.CreatedAt.AsUtc()).ToUnixTimeSeconds();
            snap = new Snapshot(student is not null && student.UserId == userId, student?.IsRegistered == true, at);
            _cache.Set(Key(userId), snap, CacheFor);
        }

        // token times are whole seconds, so a token issued in the very second of the cancel counts as older
        if (!snap.Exists || issuedAt <= snap.RevokedAtUnix) return false;
        // a signed-in session and a password-reset token need a finished sign-up; the short set-up token is for one that is not finished
        return principal.FindFirstValue("purpose") == StudentTokenService.SetupPurpose ? !snap.IsRegistered : snap.IsRegistered;
    }

    private static string Key(int userId) => $"student-session|{userId}";
}
