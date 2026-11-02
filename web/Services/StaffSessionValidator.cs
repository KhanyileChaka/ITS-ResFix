using System.Security.Claims;
using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models;
using Microsoft.Extensions.Caching.Memory;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Checks, on every staff request, that the signed-in account still exists with the same role and password.
/// Deleting a worker, changing a role or changing a password therefore ends old sessions within a minute
/// (the lookup is cached briefly so pages stay fast).
/// </summary>
public class StaffSessionValidator
{
    private record Snapshot(string? Role, string? Stamp, int? WorkerId, string? Status);

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(45);
    private readonly IMemoryCache _cache;

    public StaffSessionValidator(IMemoryCache cache) => _cache = cache;

    public void Invalidate(int userId) => _cache.Remove(Key(userId));

    public async Task<bool> IsValidAsync(ClaimsPrincipal principal, ResFixDataService data)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;

        if (!_cache.TryGetValue(Key(userId), out Snapshot? snap) || snap is null)
        {
            var user = await data.GetUserByIdAsync(userId);
            int? workerId = null;
            if (user?.Role == Roles.Worker) workerId = (await data.GetWorkerByUserIdAsync(userId))?.WorkerId;
            snap = new Snapshot(user?.Role, user is null ? null : StaffPrincipal.StampOf(user.PasswordHash), workerId, user?.AccountStatus);
            _cache.Set(Key(userId), snap, CacheFor);
        }

        if (snap.Role is null) return false;
        if (snap.Status is not null and not AccountStatuses.Active) return false; // suspended or removed: signed out
        if (snap.Role != principal.FindFirstValue(ClaimTypes.Role)) return false;
        if (snap.Stamp != principal.FindFirstValue(ResFixClaims.Stamp)) return false;
        if (snap.Role == Roles.Worker && snap.WorkerId?.ToString() != principal.FindFirstValue(ResFixClaims.WorkerId)) return false;
        return true;
    }

    private static string Key(int userId) => $"staff-session|{userId}";
}
