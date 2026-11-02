using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace ITSResFix.Staff.Models.Db;

// Supabase table models. Table and column names must match supabase/schema.sql exactly.
// "user" is a reserved word in PostgreSQL, so the User entity lives in the "users" table.

[Table("users")]
public class AppUser : BaseModel
{
    [PrimaryKey("user_id", false)]
    public int UserId { get; set; }

    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("role")]
    public string Role { get; set; } = string.Empty;

    [Column("full_name")]
    public string FullName { get; set; } = string.Empty;

    [Column("must_change_password")]
    public bool MustChangePassword { get; set; }

    [Column("cellphone")]
    public string? Cellphone { get; set; }

    [Column("account_status")]
    public string AccountStatus { get; set; } = "active";

    [Column("status_reason")]
    public string? StatusReason { get; set; }

    [Column("status_changed_at")]
    public DateTime? StatusChangedAt { get; set; }

    [Column("status_changed_by")]
    public string? StatusChangedBy { get; set; }
}

[Table("internal_worker")]
public class InternalWorker : BaseModel
{
    [PrimaryKey("worker_id", false)]
    public int WorkerId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("specialisation")]
    public string? Specialisation { get; set; }

    [Column("is_available")]
    public bool IsAvailable { get; set; } = true;

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("assigned_accommodation")]
    public string? AssignedAccommodation { get; set; }
}

[Table("student")]
public class Student : BaseModel
{
    [PrimaryKey("student_id", false)]
    public int StudentId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("student_number")]
    public string StudentNumber { get; set; } = string.Empty;

    [Column("room_number")]
    public string RoomNumber { get; set; } = string.Empty;

    [Column("cellphone")]
    public string Cellphone { get; set; } = string.Empty;

    [Column("is_registered")]
    public bool IsRegistered { get; set; }

    [Column("sms_alerts")]
    public bool SmsAlerts { get; set; }

    [Column("contact_email")]
    public string? ContactEmail { get; set; }
}

[Table("category")]
public class Category : BaseModel
{
    [PrimaryKey("category_id", false)]
    public int CategoryId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;
}

[Table("complaint_status")]
public class ComplaintStatus : BaseModel
{
    [PrimaryKey("status_id", false)]
    public int StatusId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;
}

[Table("complaint")]
public class Complaint : BaseModel
{
    [PrimaryKey("complaint_id", false)]
    public int ComplaintId { get; set; }

    [Column("student_id")]
    public int StudentId { get; set; }

    [Column("category_id")]
    public int CategoryId { get; set; }

    [Column("room_number")]
    public string RoomNumber { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("status_id")]
    public int StatusId { get; set; }

    [Column("priority")]
    public string Priority { get; set; } = Priorities.Normal;

    [Column("assigned_to")]
    public int? AssignedTo { get; set; }

    [Column("assigned_type")]
    public string? AssignedType { get; set; }

    [Column("assigned_at")]
    public DateTime? AssignedAt { get; set; }

    [Column("deadline")]
    public DateTime? Deadline { get; set; }

    [Column("timer_status")]
    public string TimerStatus { get; set; } = TimerStatuses.Pending;

    [Column("photo_url")]
    public string? PhotoUrl { get; set; }

    [Column("worker_notes")]
    public string? WorkerNotes { get; set; }

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }

    [Column("ai_checked")]
    public bool AiChecked { get; set; }

    [Column("ai_note")]
    public string? AiNote { get; set; }

    [Column("original_category_id")]
    public int? OriginalCategoryId { get; set; }

    [Column("photo_note")]
    public string? PhotoNote { get; set; }

    [Column("scheduled_visit_at")]
    public DateTime? ScheduledVisitAt { get; set; }

    [Column("appeal_status")]
    public string? AppealStatus { get; set; }

    [Column("appeal_reason")]
    public string? AppealReason { get; set; }

    [Column("appeal_response")]
    public string? AppealResponse { get; set; }

    [Column("appealed_at")]
    public DateTime? AppealedAt { get; set; }
}

[Table("student_notification")]
public class StudentNotification : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("student_id")]
    public int StudentId { get; set; }

    [Column("complaint_id")]
    public int? ComplaintId { get; set; }

    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Column("is_read")]
    public bool IsRead { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("complaint_message")]
public class ComplaintMessage : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("complaint_id")]
    public int ComplaintId { get; set; }

    [Column("sender")]
    public string Sender { get; set; } = string.Empty;   // "student" or "staff"

    [Column("sender_name")]
    public string? SenderName { get; set; }

    [Column("body")]
    public string Body { get; set; } = string.Empty;

    [Column("photo_url")]
    public string? PhotoUrl { get; set; }

    /// <summary>When the other side opened the conversation and saw this message.</summary>
    [Column("read_at")]
    public DateTime? ReadAt { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("push_subscription")]
public class PushSubscriptionRow : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("student_id")]
    public int StudentId { get; set; }

    [Column("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [Column("p256dh")]
    public string P256dh { get; set; } = string.Empty;

    [Column("auth")]
    public string Auth { get; set; } = string.Empty;

    [Column("user_agent")]
    public string? UserAgent { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("student_otp")]
public class StudentOtp : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("cellphone")]
    public string Cellphone { get; set; } = string.Empty;

    // Stores an HMAC of the code, never the code itself.
    [Column("otp_code")]
    public string OtpCode { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("used")]
    public bool Used { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("complaint_rating")]
public class ComplaintRating : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("complaint_id")]
    public int ComplaintId { get; set; }

    [Column("rating")]
    public int Rating { get; set; }

    [Column("issue_resolved")]
    public bool IssueResolved { get; set; }

    [Column("comment")]
    public string? Comment { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("external_company")]
public class ExternalCompany : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("company_name")]
    public string CompanyName { get; set; } = string.Empty;

    [Column("api_endpoint")]
    public string? ApiEndpoint { get; set; }

    [Column("contact_email")]
    public string ContactEmail { get; set; } = string.Empty;

    [Column("contact_phone")]
    public string ContactPhone { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}

[Table("auto_assignment_rule")]
public class AutoAssignmentRule : BaseModel
{
    [PrimaryKey("rule_id", false)]
    public int RuleId { get; set; }

    [Column("category_id")]
    public int CategoryId { get; set; }

    [Column("worker_id")]
    public int WorkerId { get; set; }
}

[Table("password_reset")]
public class PasswordResetRow : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("token_hash")]
    public string TokenHash { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("used_at")]
    public DateTime? UsedAt { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}
