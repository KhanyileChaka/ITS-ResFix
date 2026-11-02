namespace ITSResFix.Staff.Models;

/// <summary>Values stored in users.role.</summary>
public static class Roles
{
    public const string Student = "student";
    public const string Manager = "manager";
    public const string Worker = "worker";
}

/// <summary>Values stored in users.account_status: the levels of taking someone out of the system.</summary>
public static class AccountStatuses
{
    public const string Active = "active";        // can sign in and work
    public const string Suspended = "suspended";  // blocked for now; can be restored as they were
    public const string Removed = "removed";      // has left; history kept; can be restored but must be set up again
}

/// <summary>Primary keys of the seeded complaint_status lookup rows (see supabase/schema.sql).</summary>
public static class StatusIds
{
    public const int Submitted = 1;
    public const int InProgress = 2;
    public const int Resolved = 3;
    public const int Rejected = 4;
}

public static class Priorities
{
    public const string Normal = "Normal";
    public const string Emergency = "Emergency";
}

/// <summary>Values stored in complaint.timer_status.</summary>
public static class TimerStatuses
{
    public const string Pending = "pending";     // not yet assigned, clock not running
    public const string Active = "active";       // assigned, inside the 48h window
    public const string Expired = "expired";     // deadline passed before resolution
    public const string Completed = "completed"; // resolved
}

/// <summary>Values stored in complaint.assigned_type.</summary>
public static class AssignmentTypes
{
    public const string Auto = "auto";
    public const string Manual = "manual";
    public const string External = "external";
}

/// <summary>Custom claim types added to the auth cookie.</summary>
public static class ResFixClaims
{
    public const string WorkerId = "resfix:worker_id";
    /// <summary>Fingerprint of the password hash; a changed password ends every other session.</summary>
    public const string Stamp = "resfix:stamp";
    /// <summary>Present ("1") while the account still has a one-time password that must be replaced.</summary>
    public const string MustChange = "resfix:must_change";
}

/// <summary>Values stored in complaint.appeal_status.</summary>
public static class AppealStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Declined = "declined";
}