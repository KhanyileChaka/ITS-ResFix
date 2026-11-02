using System.Security.Claims;
using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.Db;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ITSResFix.Staff.Helpers;

/// <summary>Builds the cookie principal for a staff member (used at login and after profile edits).</summary>
public static class StaffPrincipal
{
    /// <summary>Short fingerprint of the password hash (the hash itself never goes into the cookie).</summary>
    public static string StampOf(string? passwordHash) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(passwordHash ?? "")))[..16];

    public static ClaimsPrincipal Create(AppUser user, int? workerId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.DisplayName()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(ResFixClaims.Stamp, StampOf(user.PasswordHash))
        };
        if (user.MustChangePassword) claims.Add(new Claim(ResFixClaims.MustChange, "1"));
        if (workerId is not null) claims.Add(new Claim(ResFixClaims.WorkerId, workerId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
