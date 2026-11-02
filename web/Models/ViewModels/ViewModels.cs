using System.ComponentModel.DataAnnotations;
using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models.Db;

namespace ITSResFix.Staff.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

/// <summary>How the 48-hour timer should be shown for a complaint.</summary>
public record TimerInfo(string Label, string CssClass, bool IsExpired);

public record WorkerOption(int WorkerId, string Name, string? Specialisation, bool IsAvailable);

/// <summary>A complaint joined with its lookup values, ready for display.</summary>
public class ComplaintRow
{
    public required Complaint Complaint { get; init; }
    public string CategoryName { get; init; } = "-";
    public string? OriginalCategoryName { get; init; }
    public string StatusName { get; init; } = "-";
    public bool IsRejected => Complaint.StatusId == StatusIds.Rejected;
    public bool AppealPending => Complaint.AppealStatus == AppealStatuses.Pending;
    /// <summary>The student wrote last and nobody on staff has answered yet.</summary>
    public bool AwaitingReply { get; init; }
    public string StudentName { get; init; } = "-";
    public string StudentNumber { get; init; } = "-";
    public string? StudentCellphone { get; init; }
    public string? WorkerName { get; init; }
    public required TimerInfo Timer { get; init; }

    public bool IsEmergency => Complaint.Priority == Priorities.Emergency;
}

/// <summary>Data for the staff conversation panel shown on the manager and worker complaint pages.</summary>
public class ConversationViewModel
{
    public int ComplaintId { get; init; }
    public string Controller { get; init; } = "Manager";
    public bool CanReply { get; init; }
    public List<ComplaintMessage> Messages { get; init; } = new();
}

public class ManagerDashboardViewModel
{
    public int Total { get; init; }
    public int Submitted { get; init; }
    public int InProgress { get; init; }
    public int Resolved { get; init; }
    public int Rejected { get; init; }
    public int Emergencies { get; init; }
    public int Overdue { get; init; }
    public int Unassigned { get; init; }
    public int Appeals { get; init; }
    public int AwaitingReply { get; init; }
    public int? OnTimeRate { get; init; }
    public List<DateTime> Days { get; init; } = new();
    public List<int> PerDay { get; init; } = new();
    public List<WorkerLoad> Workers { get; init; } = new();
    public List<ComplaintRow> Recent { get; init; } = new();
}

public record WorkerLoad(string Name, bool Available, int Open, int Overdue);

public class ComplaintListViewModel
{
    public List<ComplaintRow> Complaints { get; init; } = new();
    public bool EmergencyOnly { get; init; }
    public int? StatusFilter { get; init; }
    public string? SearchQuery { get; init; }
    public int? CategoryFilter { get; init; }
    public string? TimerStatusFilter { get; init; }
    public string? PriorityFilter { get; init; }
    public List<Category> Categories { get; init; } = new();
    public int CountAll { get; init; }
    public int CountSubmitted { get; init; }
    public int CountInProgress { get; init; }
    public int CountResolved { get; init; }
    public int CountRejected { get; init; }
}

public class ExpiredComplaintsViewModel
{
    public List<ComplaintRow> Complaints { get; init; } = new();
    public List<WorkerOption> Workers { get; init; } = new();
}

public class StudentRow
{
    public int StudentId { get; init; }
    public string? Email { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string StudentNumber { get; init; } = string.Empty;
    public string RoomNumber { get; init; } = string.Empty;
    public string Cellphone { get; init; } = string.Empty;
    public bool IsRegistered { get; init; }
    public string AccountStatus { get; init; } = "active";
    public string? StatusReason { get; init; }
}

public class AddStudentViewModel
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(120)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Student number is required.")]
    [RegularExpression(@"^\d{6,12}$", ErrorMessage = "Student number must be 6–12 digits.")]
    [Display(Name = "Student Number")]
    public string StudentNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Room number is required.")]
    [StringLength(20)]
    [Display(Name = "Room Number")]
    public string RoomNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cellphone number is required.")]
    [RegularExpression(@"^(\+27|0)\d{9}$", ErrorMessage = "Enter a valid SA number, e.g. 0712345678.")]
    [Display(Name = "Cellphone Number")]
    public string Cellphone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Enter a valid email address, or leave it empty.")]
    [RegularExpression(@"^[^\s@<>""',;()\[\]\\]+@[^\s@<>""',;()\[\]\\]+\.[^\s@<>""',;()\[\]\\]+$", ErrorMessage = "Enter a valid email address, or leave it empty.")]
    [StringLength(120)]
    [Display(Name = "Email (optional)")]
    public string? Email { get; set; }
}

public class RuleRow
{
    public int RuleId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string WorkerName { get; init; } = string.Empty;
    public string? Specialisation { get; init; }
    public string Priority => CategoryName switch
    {
        "Electrical" or "Plumbing" or "Pest Control" or "Doors & Locks" => "High",
        _ => "Normal"
    };
}

// ---- Workers management ----
public class WorkerManagementRow
{
    public int WorkerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Specialisation { get; init; }
    public string? Phone { get; init; }
    public bool IsAvailable { get; init; }
    public int AssignedJobs { get; init; }
    public string AccountStatus { get; init; } = "active";
    public string? StatusReason { get; init; }
}

public class WorkersManagementViewModel
{
    public List<WorkerManagementRow> Workers { get; init; } = new();
}

public class AddWorkerViewModel
{
    [Required(ErrorMessage = "Full name is required."), StringLength(120)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required."), EmailAddress]
    [RegularExpression(@"^[^\s@<>""',;()\[\]\x5C]+@[^\s@<>""',;()\[\]\x5C]+\.[^\s@<>""',;()\[\]\x5C]+$", ErrorMessage = "Enter a valid email address, like name@example.com.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Specialisation")]
    [StringLength(80)]
    public string? Specialisation { get; set; }

    [Display(Name = "Phone")]
    [RegularExpression(@"^(\+27|0)[\d ]{9,12}$", ErrorMessage = "Enter a valid SA number.")]
    public string? Phone { get; set; }
}

// ---- External company ----
public class ExternalCompanyPageViewModel
{
    public ExternalCompanyViewModel Company { get; init; } = new();
    public List<ComplaintRow> ReassignedComplaints { get; init; } = new();
}

public class ExternalCompanyViewModel
{
    public bool Exists { get; init; }

    [Required(ErrorMessage = "Company name is required."), StringLength(120)]
    [Display(Name = "Company Name")]
    public string CompanyName { get; set; } = string.Empty;

    [Display(Name = "API Endpoint")]
    [StringLength(300)]
    [RegularExpression(@"^https://[A-Za-z0-9.-]+(:\d+)?(/[^\s]*)?$", ErrorMessage = "Use a full https:// address, or leave this empty.")]
    public string? ApiEndpoint { get; set; }

    [Required(ErrorMessage = "Contact email is required."), EmailAddress]
    [Display(Name = "Contact Email")]
    public string ContactEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact phone is required.")]
    [Display(Name = "Contact Phone")]
    [StringLength(30)]
    public string ContactPhone { get; set; } = string.Empty;

    [Display(Name = "Status")]
    public bool IsActive { get; set; } = true;
}

// ---- Categories management ----
public class CategoriesViewModel
{
    public List<Category> Categories { get; set; } = new();
    /// <summary>Complaints filed under each category id.</summary>
    public Dictionary<int, int> Counts { get; set; } = new();

    [Required(ErrorMessage = "Category name is required."), StringLength(80)]
    [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9 &/\-'.,()]*$", ErrorMessage = "Use letters, numbers and spaces only (& / - . , are fine).")]
    [Display(Name = "Category Name")]
    public string NewName { get; set; } = string.Empty;
}

// ---- Reassign complaint ----
public class ReassignViewModel
{
    public required ComplaintRow Row { get; init; }
    public List<WorkerManagementRow> Workers { get; init; } = new();
    public string? ExternalCompanyName { get; init; }
    public string? ExternalContactEmail { get; init; }
    public string? ExternalContactPhone { get; init; }

    [Display(Name = "Reason for Reassignment")]
    public string Reason { get; set; } = "Worker did not respond within 48 hours";

    public int? SelectedWorkerId { get; set; }
    public bool UseExternalCompany { get; set; }
}

public class AssignmentRulesViewModel
{
    public List<RuleRow> Rules { get; init; } = new();
    public string? CompanyName { get; init; }
    public List<Category> Categories { get; init; } = new();
    public List<WorkerOption> Workers { get; init; } = new();

    [Required, Display(Name = "Category")]
    public int? CategoryId { get; set; }

    [Required, Display(Name = "Internal Worker")]
    public int? WorkerId { get; set; }
}

/// <summary>Used by the worker Dashboard and My Assigned Jobs pages.</summary>
public class WorkerJobsViewModel
{
    public List<ComplaintRow> Jobs { get; init; } = new();
    public string TableHeading { get; init; } = "My Assigned Jobs";
    public string Filter { get; init; } = "all";
    public string? Query { get; init; }
    public bool IsAvailable { get; init; } = true;
    /// <summary>All of the worker's jobs before the Jobs page filters are applied.</summary>
    public List<ComplaintRow> AllJobs { get; init; } = new();

    public int NewJobs => Jobs.Count(j => j.Complaint.StatusId == StatusIds.Submitted);
    public int InProgress => Jobs.Count(j => j.Complaint.StatusId == StatusIds.InProgress);
    public int CompletedToday => Jobs.Count(j => j.Complaint.ResolvedAt.IsSaToday());

    /// <summary>Open jobs that are emergencies or already past their deadline.</summary>
    public int Urgent => Jobs.Count(j => j.Complaint.StatusId != StatusIds.Resolved && (j.IsEmergency || j.Timer.IsExpired));

    /// <summary>Open jobs with less than 12 hours left (shown as warnings).</summary>
    public List<ComplaintRow> DueSoon => Jobs.Where(j => j.Timer.CssClass == "timer-warning").ToList();

    /// <summary>The open job with the nearest deadline, for the "Update Status" shortcut.</summary>
    public ComplaintRow? NextJob => Jobs
        .Where(j => j.Complaint.StatusId != StatusIds.Resolved)
        .OrderBy(j => j.Complaint.Deadline ?? DateTime.MaxValue)
        .FirstOrDefault();
}

public class WorkerHistoryViewModel
{
    public List<ComplaintRow> Jobs { get; init; } = new();
    public string Filter { get; init; } = "all";
    public int TotalCompleted { get; init; }
    public int? OnTimeRate { get; init; }
}

public class EditProfileViewModel
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(120)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [RegularExpression(@"^(\+27|0)[\d ]{9,12}$", ErrorMessage = "Enter a valid SA number, e.g. 071 234 5678.")]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current Password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm New Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class WorkerProfileViewModel
{
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Specialisation { get; init; }
    public string? AssignedAccommodation { get; init; }
    public bool AutoAssignmentEnabled { get; init; }
    public int? ResponseRate { get; init; }

    public EditProfileViewModel Edit { get; set; } = new();
    public ChangePasswordViewModel Password { get; set; } = new();
}

public class WorkerComplaintViewModel
{
    public required ComplaintRow Row { get; init; }

    [Display(Name = "Status")]
    public int StatusId { get; set; }

    [StringLength(2000)]
    [Display(Name = "Work Notes")]
    public string? Notes { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter your email address or cellphone number.")]
    [StringLength(120, ErrorMessage = "That is too long.")]
    [Display(Name = "Email address or cellphone number")]
    public string Identifier { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required] public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}


// ---- Reports ----
public record ReportCount(string Label, int Count, int Done = 0);
public record WorkerReport(string Name, int Assigned, int Completed, int? OnTimeRate, double? AvgHours, double? AvgRating);

public class ReportsViewModel
{
    /// <summary>Days covered by the report; 0 means all time.</summary>
    public int Range { get; init; }
    public int Total { get; init; }
    public int Open { get; init; }
    public int Resolved { get; init; }
    public int Rejected { get; init; }
    public int Emergencies { get; init; }
    public int? ResolutionRate { get; init; }
    public int? OnTimeRate { get; init; }
    public double? AvgHours { get; init; }
    public double? AvgRating { get; init; }
    public int RatingCount { get; init; }
    public List<ReportCount> ByCategory { get; init; } = new();
    public List<ReportCount> Trend { get; init; } = new();
    public string TrendUnit { get; init; } = "day";
    public List<WorkerReport> Workers { get; init; } = new();
    public List<ReportCount> Rooms { get; init; } = new();
    public int AiRejected { get; init; }
    public int AiRecategorised { get; init; }
    public int AppealsPending { get; init; }
    public int AppealsApproved { get; init; }
    public int AppealsDeclined { get; init; }
    public int AutoAssigned { get; init; }
    public int ManualAssigned { get; init; }
    public int Overdue { get; init; }
}
