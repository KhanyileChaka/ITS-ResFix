using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.Db;
using ITSResFix.Staff.Models.ViewModels;

namespace ITSResFix.Staff.Helpers;

public static class PasswordHasher
{
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

    public static bool Verify(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Stored value is not a bcrypt hash.
            return false;
        }
    }
}

public static class TimerHelper
{
    public const int DeadlineHours = 48;

    public static TimerInfo Compute(DateTime? deadline, int statusId, DateTime nowUtc)
    {
        if (statusId == StatusIds.Resolved) return new("Completed", "timer-done", false);
        if (deadline is null) return new("Not assigned", "timer-idle", false);

        var left = deadline.Value.AsUtc() - nowUtc;
        if (left <= TimeSpan.Zero) return new("EXPIRED", "timer-expired", true);

        var label = left.TotalHours >= 1 ? $"{(int)left.TotalHours}h left" : $"{Math.Max(1, (int)left.TotalMinutes)}m left";
        return new(label, left.TotalHours < 12 ? "timer-warning" : "timer-ok", false);
    }

    /// <summary>"36 hours" / "45 minutes" until the deadline, or "0 minutes" once passed.</summary>
    public static string RemainingText(DateTime? deadline)
    {
        if (deadline is null) return "-";
        var left = deadline.Value.AsUtc() - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) return "0 minutes";
        var hours = (int)left.TotalHours;
        return hours >= 1 ? $"{hours} hour{(hours == 1 ? "" : "s")}" : $"{Math.Max(1, (int)left.TotalMinutes)} minutes";
    }

    /// <summary>Share (0–100) of the response window still remaining.</summary>
    public static int PercentRemaining(DateTime? assignedAt, DateTime? deadline)
    {
        if (deadline is null) return 0;
        var end = deadline.Value.AsUtc();
        var start = assignedAt?.AsUtc() ?? end.AddHours(-DeadlineHours);
        var window = (end - start).TotalSeconds;
        if (window <= 0) return 0;
        return (int)Math.Clamp(Math.Round(100 * (end - DateTime.UtcNow).TotalSeconds / window), 0, 100);
    }
}

public static class DisplayHelper
{
    private static readonly TimeZoneInfo SaTime = FindSaTime();
    public static TimeZoneInfo SaZone => SaTime;

    private static TimeZoneInfo FindSaTime()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }

    /// <summary>Supabase returns timestamptz values; treat unspecified kinds as UTC.</summary>
    public static DateTime AsUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static string ToSaTime(this DateTime? value, string format = "dd MMM yyyy, HH:mm") =>
        value is null ? "-" : TimeZoneInfo.ConvertTimeFromUtc(value.Value.AsUtc(), SaTime).ToString(format);

    /// <summary>Turns a wall-clock time typed by a worker (South African time) into UTC.</summary>
    public static DateTime FromSaLocal(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), SaTime);

    public static string ToIsoUtc(this DateTime? value) => value is null ? string.Empty : value.Value.AsUtc().ToString("o");

    public static bool IsSaToday(this DateTime? value) =>
        value is not null &&
        TimeZoneInfo.ConvertTimeFromUtc(value.Value.AsUtc(), SaTime).Date == TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SaTime).Date;

    /// <summary>Job reference shown to workers, e.g. 1 → "C001".</summary>
    public static string JobCode(int complaintId) => $"C{complaintId:000}";

    public static string WorkerStatusLabel(int statusId) => statusId switch
    {
        StatusIds.Submitted => "New",
        StatusIds.InProgress => "In Progress",
        StatusIds.Resolved => "Completed",
        _ => "Unknown"
    };

    /// <summary>Percentage of resolved jobs finished before their deadline, or null if none are resolved.</summary>
    public static int? OnTimeRate(IEnumerable<Complaint> complaints)
    {
        var resolved = complaints.Where(c => c.StatusId == StatusIds.Resolved && c.ResolvedAt is not null).ToList();
        if (resolved.Count == 0) return null;
        var onTime = resolved.Count(c => c.Deadline is null || c.ResolvedAt!.Value.AsUtc() <= c.Deadline.Value.AsUtc());
        return (int)Math.Round(100.0 * onTime / resolved.Count);
    }

    public static string RateLabel(int rate) => rate >= 90 ? "Excellent" : rate >= 75 ? "Good" : "Needs improvement";

    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    public static string DisplayName(this AppUser? user) =>
        user is null ? "-" : string.IsNullOrWhiteSpace(user.FullName) ? user.Email : user.FullName;

    public static string StatusCss(int statusId) => statusId switch
    {
        StatusIds.Submitted => "status-submitted",
        StatusIds.InProgress => "status-progress",
        StatusIds.Resolved => "status-resolved",
        StatusIds.Rejected => "status-rejected",
        _ => "status-unknown"
    };

    public static string PriorityCss(string priority) =>
        priority == Priorities.Emergency ? "priority-emergency" : "priority-normal";

    /// <summary>Photo URLs come from the student app; only render absolute https links.</summary>
    public static bool IsHttpsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public static string Truncate(this string? text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..max].TrimEnd() + "…";
}
