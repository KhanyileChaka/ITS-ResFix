using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.Db;
using ITSResFix.Staff.Models.ViewModels;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Data access over the shared Supabase database. Uses the server-side service_role client,
/// so every authorization decision (who may see or change what) is made here or in the controllers.
/// </summary>
public class ResFixDataService
{
    // Never select password_hash except when verifying a login.
    private const string PublicUserColumns = "user_id,email,role,full_name,account_status,status_reason";

    private readonly Supabase.Client _client;
    private readonly NotificationService _notify;
    private readonly EmailService _email;
    private readonly string _studentEmailDomain;

    private readonly WorkerNotifier _workers;

    public ResFixDataService(Supabase.Client client, IConfiguration config, NotificationService notify, EmailService email, WorkerNotifier workers)
    {
        _workers = workers;
        _client = client;
        _email = email;
        _notify = notify;
        _studentEmailDomain = config["Students:EmailDomain"] ?? "students.resfix.local";
    }

    // ---------------- Users & workers ----------------

    public async Task<AppUser?> GetUserForLoginAsync(string email)
    {
        var result = await _client.From<AppUser>()
            .Filter("email", Operator.Equals, email.Trim().ToLowerInvariant())
            .Limit(1)
            .Get();
        return result.Models.FirstOrDefault();
    }

    public async Task<InternalWorker?> GetWorkerByUserIdAsync(int userId)
    {
        var result = await _client.From<InternalWorker>()
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Limit(1)
            .Get();
        return result.Models.FirstOrDefault();
    }

    /// <summary>Full user row, including password_hash (for password changes).</summary>
    public async Task<AppUser?> GetUserByIdAsync(int userId)
    {
        var result = await _client.From<AppUser>()
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Limit(1)
            .Get();
        return result.Models.FirstOrDefault();
    }

    public Task UpdateUserAsync(AppUser user) => _client.From<AppUser>().Update(user);

    public async Task<InternalWorker?> GetWorkerAsync(int workerId)
    {
        var result = await _client.From<InternalWorker>()
            .Filter("worker_id", Operator.Equals, workerId.ToString())
            .Limit(1)
            .Get();
        return result.Models.FirstOrDefault();
    }

    /// <summary>False when the worker does not exist or their account is suspended or removed.</summary>
    public async Task<bool> IsWorkerActiveAsync(int workerId)
    {
        var worker = await GetWorkerAsync(workerId);
        if (worker is null) return false;
        var user = await GetUserByIdAsync(worker.UserId);
        return user is not null && user.AccountStatus == AccountStatuses.Active;
    }

    public Task UpdateWorkerAsync(InternalWorker worker) => _client.From<InternalWorker>().Update(worker);

    /// <summary>True when at least one auto-assignment rule routes complaints to this worker.</summary>
    public async Task<bool> WorkerHasAutoAssignmentAsync(int workerId)
    {
        var result = await _client.From<AutoAssignmentRule>()
            .Filter("worker_id", Operator.Equals, workerId.ToString())
            .Limit(1)
            .Get();
        return result.Models.Count > 0;
    }

    /// <summary>The workers who can be given work. Pass includeInactive to also get suspended and removed ones (for names in history).</summary>
    public async Task<List<WorkerOption>> GetWorkersAsync(bool includeInactive = false)
    {
        var workers = (await _client.From<InternalWorker>().Get()).Models;
        var users = await GetUsersByIdsAsync(workers.Select(w => w.UserId));
        return workers
            .Where(w => includeInactive || users.GetValueOrDefault(w.UserId)?.AccountStatus is null or AccountStatuses.Active)
            .Select(w => new WorkerOption(w.WorkerId, users.GetValueOrDefault(w.UserId).DisplayName(), w.Specialisation, w.IsAvailable))
            .OrderBy(w => w.Name)
            .ToList();
    }

    private async Task<Dictionary<int, AppUser>> GetUsersByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().Cast<object>().ToList();
        if (idList.Count == 0) return new();

        // long id lists go in the web address, so ask in chunks to stay under URL length limits
        var chunks = await Task.WhenAll(idList.Chunk(100).Select(chunk => _client.From<AppUser>()
            .Select(PublicUserColumns)
            .Filter("user_id", Operator.In, chunk.ToList())
            .Get()));
        return chunks.SelectMany(r => r.Models).ToDictionary(u => u.UserId);
    }

    // ---------------- Lookups ----------------

    public async Task<List<Category>> GetCategoriesAsync() =>
        (await _client.From<Category>().Order("name", Ordering.Ascending).Get()).Models;

    private async Task<Dictionary<int, string>> GetStatusNamesAsync() =>
        (await _client.From<ComplaintStatus>().Get()).Models.ToDictionary(s => s.StatusId, s => s.Name);

    // ---------------- Complaints ----------------

    /// <summary>Flags overdue complaints as expired in the database (see mark_expired_complaints in schema.sql).</summary>
    public Task SyncExpiredTimersAsync() => _client.Rpc("mark_expired_complaints", null);

    public async Task<List<ComplaintRow>> GetComplaintsAsync(bool emergencyOnly = false, int? statusId = null)
    {
        var query = _client.From<Complaint>().Order("created_at", Ordering.Descending);
        if (emergencyOnly) query = query.Filter("priority", Operator.Equals, Priorities.Emergency);
        if (statusId is not null) query = query.Filter("status_id", Operator.Equals, statusId.Value.ToString());

        // The database hands back at most 1000 rows per request, so read every page or totals would silently stop at 1000.
        var all = new List<Complaint>();
        const int page = 1000;
        for (var from = 0; ; from += page)
        {
            var batch = (await query.Range(from, from + page - 1).Get()).Models;
            all.AddRange(batch);
            if (batch.Count < page) break;
        }
        return await EnrichAsync(all);
    }

    public async Task<List<ComplaintRow>> GetExpiredComplaintsAsync()
    {
        var result = await _client.From<Complaint>()
            .Filter("deadline", Operator.LessThan, DateTime.UtcNow.ToString("o"))
            .Filter("status_id", Operator.NotEqual, StatusIds.Resolved.ToString())
            .Order("deadline", Ordering.Ascending)
            .Get();
        return await EnrichAsync(result.Models);
    }

    public async Task<List<ComplaintRow>> GetComplaintsForWorkerAsync(int workerId)
    {
        var result = await _client.From<Complaint>()
            .Filter("assigned_to", Operator.Equals, workerId.ToString())
            .Order("deadline", Ordering.Ascending)
            .Get();
        return await EnrichAsync(result.Models);
    }

    public async Task<Complaint?> GetComplaintAsync(int complaintId)
    {
        var result = await _client.From<Complaint>()
            .Filter("complaint_id", Operator.Equals, complaintId.ToString())
            .Limit(1)
            .Get();
        return result.Models.FirstOrDefault();
    }

    public async Task<ComplaintRow?> GetComplaintRowAsync(int complaintId)
    {
        var complaint = await GetComplaintAsync(complaintId);
        return complaint is null ? null : (await EnrichAsync(new List<Complaint> { complaint })).Single();
    }

    public async Task<List<ComplaintRow>> GetExternalComplaintsAsync()
    {
        var result = await _client.From<Complaint>()
            .Filter("assigned_type", Operator.Equals, AssignmentTypes.External)
            .Order("created_at", Ordering.Descending)
            .Get();
        return await EnrichAsync(result.Models);
    }

    /// <summary>Escalate to external company: clears the internal worker assignment.</summary>
    public record ExternalResult(bool Ok, bool Emailed, string? CompanyName, string? ContactEmail, string? ContactPhone);

    /// <summary>Marks the complaint as handled by the external company and emails them the details when email is set up.</summary>
    public async Task<ExternalResult> ReassignToExternalAsync(int complaintId)
    {
        var complaint = await GetComplaintAsync(complaintId);
        if (complaint is null || complaint.StatusId is StatusIds.Resolved or StatusIds.Rejected) return new(false, false, null, null, null);
        complaint.AssignedTo = null;
        complaint.AssignedType = AssignmentTypes.External;
        complaint.TimerStatus = TimerStatuses.Expired;
        await _client.From<Complaint>().Update(complaint);

        var company = await GetExternalCompanyAsync();
        var emailed = false;
        if (company is { IsActive: true } && !string.IsNullOrWhiteSpace(company.ContactEmail))
        {
            var category = (await GetCategoriesAsync()).FirstOrDefault(c => c.CategoryId == complaint.CategoryId)?.Name ?? "Uncategorised";
            var (html, text) = EmailTemplates.Escalation(company.CompanyName, $"C{complaint.ComplaintId:D3}", category,
                complaint.RoomNumber, complaint.Priority, complaint.Description, complaint.PhotoUrl);
            emailed = await _email.SendAsync(company.ContactEmail, $"ITS ResFix maintenance request C{complaint.ComplaintId:D3}", text, html);
        }
        return new(true, emailed, company?.CompanyName, company?.ContactEmail, company?.ContactPhone);
    }

    /// <summary>
    /// Manager overrides an AI rejection: complaint goes back to Submitted, is auto-assigned if the category has a rule,
    /// and the student is told. Returns null when the complaint is missing or not rejected, otherwise the assigned worker's name (or "").
    /// </summary>
    public async Task<string?> AcceptRejectedAsync(int complaintId, string? managerResponse = null)
    {
        var c = await GetComplaintAsync(complaintId);
        if (c is null || c.StatusId != StatusIds.Rejected) return null;

        var wasAppeal = c.AppealStatus == AppealStatuses.Pending;
        if (wasAppeal)
        {
            c.AppealStatus = AppealStatuses.Approved;
            c.AppealResponse = string.IsNullOrWhiteSpace(managerResponse) ? null : managerResponse.Trim();
        }

        c.StatusId = StatusIds.Submitted;
        c.AiChecked = true;
        c.AiNote = ((c.AiNote ?? "").TrimEnd() + " Overridden and accepted by the manager.").Trim();
        c.TimerStatus = TimerStatuses.Pending;
        c.AssignedTo = null; c.AssignedType = null; c.AssignedAt = null; c.Deadline = null;

        string workerName = "";
        var rule = (await _client.From<AutoAssignmentRule>()
            .Filter("category_id", Operator.Equals, c.CategoryId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        if (rule is not null && await IsWorkerActiveAsync(rule.WorkerId))
        {
            var now = DateTime.UtcNow;
            c.AssignedTo = rule.WorkerId; c.AssignedType = AssignmentTypes.Auto;
            c.AssignedAt = now; c.Deadline = now.AddHours(TimerHelper.DeadlineHours);
            c.TimerStatus = TimerStatuses.Active;
            workerName = (await GetWorkerOptionAsync(rule.WorkerId))?.Name ?? "";
        }

        await _client.From<Complaint>().Update(c);
        _ = _workers.NotifyAssignedAsync(c);
        var code = $"C{c.ComplaintId:D3}";
        var message = wasAppeal
            ? $"Your appeal for {code} was approved and your request is now in the queue." + (c.AppealResponse is null ? "" : $" Manager: {c.AppealResponse}")
            : $"Your complaint {code} was reviewed by the manager and accepted.";
        await _notify.NotifyAsync(c.StudentId, c.ComplaintId, message, sms: true);
        return workerName;
    }

    /// <summary>Manager declines a pending appeal. Returns false when there is no pending appeal on that complaint.</summary>
    public async Task<bool> DeclineAppealAsync(int complaintId, string? managerResponse)
    {
        var c = await GetComplaintAsync(complaintId);
        if (c is null || c.AppealStatus != AppealStatuses.Pending) return false;
        c.AppealStatus = AppealStatuses.Declined;
        c.AppealResponse = string.IsNullOrWhiteSpace(managerResponse) ? null : managerResponse.Trim();
        await _client.From<Complaint>().Update(c);
        await _notify.NotifyAsync(c.StudentId, c.ComplaintId,
            $"Your appeal for C{c.ComplaintId:D3} was declined." + (c.AppealResponse is null ? "" : $" Manager: {c.AppealResponse}"), sms: true);
        return true;
    }

    // ---------- Conversation with the student ----------

    public async Task<List<ComplaintMessage>> GetMessagesAsync(int complaintId) =>
        (await _client.From<ComplaintMessage>().Filter("complaint_id", Operator.Equals, complaintId.ToString())
            .Order("created_at", Ordering.Ascending).Get()).Models;

    /// <summary>A manager or worker writes to the student. Returns false when the complaint does not exist or is closed to messages.</summary>
    public async Task<bool> PostStaffMessageAsync(int complaintId, string senderName, string body)
    {
        body = (body ?? "").Trim();
        if (body.Length == 0) return false;
        if (body.Length > 1000) body = body[..1000];
        var c = await GetComplaintAsync(complaintId);
        if (c is null || c.StatusId == StatusIds.Rejected) return false;
        // Each staff message can send an SMS, so cap how many one request can get (stops spamming a student).
        var since = DateTime.UtcNow.AddHours(-1).ToString("o");
        var recent = (await _client.From<ComplaintMessage>()
            .Filter("complaint_id", Operator.Equals, complaintId.ToString())
            .Filter("sender", Operator.Equals, "staff")
            .Filter("created_at", Operator.GreaterThan, since)
            .Get()).Models.Count;
        if (recent >= 10) return false;
        await _client.From<ComplaintMessage>().Insert(new ComplaintMessage { ComplaintId = complaintId, Sender = "staff", SenderName = senderName, Body = body });
        var preview = body.Length > 90 ? body[..90] + "..." : body;
        await _notify.NotifyAsync(c.StudentId, complaintId, $"New message about C{complaintId:D3} from {senderName}: {preview}", sms: true);
        return true;
    }

    private async Task<WorkerOption?> GetWorkerOptionAsync(int workerId) =>
        (await GetWorkersAsync()).FirstOrDefault(w => w.WorkerId == workerId);

    /// <summary>Manager reassignment: new worker gets a fresh 48-hour window.</summary>
    public async Task<bool> ReassignAsync(int complaintId, int workerId)
    {
        var complaint = await GetComplaintAsync(complaintId);
        // Rejected requests go back through "accept anyway" or an appeal, never straight to a worker.
        if (complaint is null || complaint.StatusId is StatusIds.Resolved or StatusIds.Rejected) return false;

        var now = DateTime.UtcNow;
        complaint.AssignedTo = workerId;
        complaint.AssignedType = AssignmentTypes.Manual;
        complaint.AssignedAt = now;
        complaint.Deadline = now.AddHours(TimerHelper.DeadlineHours);
        complaint.TimerStatus = TimerStatuses.Active;
        await _client.From<Complaint>().Update(complaint);
        _ = _workers.NotifyAssignedAsync(complaint); // email + SMS in the background: the manager's page does not wait for them
        return true;
    }

    /// <summary>Worker progress update. Returns false if the complaint is not assigned to this worker.</summary>
    public async Task<bool> UpdateWorkProgressAsync(int complaintId, int workerId, int statusId, string? notes)
    {
        var complaint = await GetComplaintAsync(complaintId);
        if (complaint is null || complaint.AssignedTo != workerId) return false;
        // A rejected request is closed by the AI review or the manager; workers cannot bring it back.
        if (complaint.StatusId == StatusIds.Rejected) return false;

        var previousStatus = complaint.StatusId;
        var now = DateTime.UtcNow;
        complaint.StatusId = statusId;
        complaint.WorkerNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (statusId == StatusIds.Resolved)
        {
            complaint.ResolvedAt ??= now;
            complaint.TimerStatus = TimerStatuses.Completed;
        }
        else
        {
            // Re-opened (or never closed): the timer state follows the deadline again.
            complaint.ResolvedAt = null;
            complaint.TimerStatus = complaint.Deadline is null ? TimerStatuses.Pending
                : complaint.Deadline.Value.AsUtc() < now ? TimerStatuses.Expired
                : TimerStatuses.Active;
        }
        await _client.From<Complaint>().Update(complaint);
        if (statusId == previousStatus) return true; // saving notes again must not text the student again
        if (statusId == StatusIds.Resolved)
            await _notify.AlertOnlyAsync(complaint.StudentId, complaintId, $"Your request C{complaintId:D3} has been resolved. Open the app to rate the repair.", sms: true);
        else if (statusId == StatusIds.InProgress)
            await _notify.AlertOnlyAsync(complaint.StudentId, complaintId, $"Your request C{complaintId:D3} is now in progress.", sms: false);
        return true;
    }

    /// <summary>Star rating (1-5) students gave each repaired complaint, by complaint id.</summary>
    public async Task<Dictionary<int, int>> GetRatingsAsync() =>
        (await _client.From<ComplaintRating>().Get()).Models
            .GroupBy(r => r.ComplaintId).ToDictionary(g => g.Key, g => g.First().Rating);

    public async Task<bool> SetAvailabilityAsync(int workerId, bool available)
    {
        var worker = await GetWorkerAsync(workerId);
        if (worker is null) return false;
        worker.IsAvailable = available;
        await UpdateWorkerAsync(worker);
        return true;
    }

    public enum VisitResult { Saved, NotFound, Closed, TooSoon, TooFar }

    /// <summary>Records when the assigned worker plans to attend and tells the student.</summary>
    public async Task<VisitResult> ScheduleVisitAsync(int complaintId, int workerId, DateTime visitUtc)
    {
        var c = await GetComplaintAsync(complaintId);
        if (c is null || c.AssignedTo != workerId) return VisitResult.NotFound;
        if (c.StatusId is StatusIds.Resolved or StatusIds.Rejected) return VisitResult.Closed;
        var now = DateTime.UtcNow;
        if (visitUtc < now.AddMinutes(-5)) return VisitResult.TooSoon;
        if (visitUtc > now.AddDays(7)) return VisitResult.TooFar;

        c.ScheduledVisitAt = visitUtc;
        await _client.From<Complaint>().Update(c);
        var when = TimeZoneInfo.ConvertTimeFromUtc(visitUtc, DisplayHelper.SaZone).ToString("ddd d MMM 'at' HH:mm");
        await _notify.NotifyAsync(c.StudentId, complaintId, $"Maintenance plans to visit for C{complaintId:D3} on {when}.", sms: true);
        return VisitResult.Saved;
    }

    /// <summary>Runs an "in (...)" query in chunks of 100 ids (the ids travel in the web address) and joins the rows.</summary>
    private static async Task<List<T>> FetchInChunksAsync<T>(IEnumerable<object> ids, Func<List<object>, Task<Supabase.Postgrest.Responses.ModeledResponse<T>>> fetch)
        where T : Supabase.Postgrest.Models.BaseModel, new()
    {
        var results = await Task.WhenAll(ids.Distinct().Chunk(100).Select(chunk => fetch(chunk.ToList())));
        return results.SelectMany(r => r.Models).ToList();
    }

    private async Task<List<ComplaintRow>> EnrichAsync(List<Complaint> complaints)
    {
        if (complaints.Count == 0) return new();

        // Run all lookup queries in parallel to avoid sequential timeout
        var categoriesTask = GetCategoriesAsync();
        var statusesTask = GetStatusNamesAsync();
        var workersTask = GetWorkersAsync(includeInactive: true);

        var studentIds = complaints.Select(c => c.StudentId).Distinct().Cast<object>().ToList();
        var studentsTask = FetchInChunksAsync(studentIds, chunk => _client.From<Student>().Filter("student_id", Operator.In, chunk).Get());
        var complaintIds = complaints.Select(c => (object)c.ComplaintId).ToList();
        var messagesTask = FetchInChunksAsync(complaintIds, chunk => _client.From<ComplaintMessage>().Filter("complaint_id", Operator.In, chunk).Get());

        await Task.WhenAll(categoriesTask, statusesTask, workersTask, studentsTask, messagesTask);
        // newest message per complaint: a student message with no staff answer yet means the request needs a reply
        var lastSender = messagesTask.Result.OrderByDescending(m => m.CreatedAt).GroupBy(m => m.ComplaintId).ToDictionary(g => g.Key, g => g.First().Sender);

        var categories = categoriesTask.Result.ToDictionary(c => c.CategoryId, c => c.Name);
        var statuses = statusesTask.Result;
        var workers = workersTask.Result.ToDictionary(w => w.WorkerId);
        var students = studentsTask.Result.ToDictionary(s => s.StudentId);
        var studentUsers = await GetUsersByIdsAsync(students.Values.Select(s => s.UserId));

        var now = DateTime.UtcNow;
        return complaints.Select(c =>
        {
            students.TryGetValue(c.StudentId, out var student);
            return new ComplaintRow
            {
                Complaint = c,
                AwaitingReply = lastSender.GetValueOrDefault(c.ComplaintId) == "student",
                CategoryName = categories.GetValueOrDefault(c.CategoryId, "Uncategorised"),
                OriginalCategoryName = c.OriginalCategoryId is int orig ? categories.GetValueOrDefault(orig) : null,
                StatusName = statuses.GetValueOrDefault(c.StatusId, "Unknown"),
                StudentNumber = student?.StudentNumber ?? "-",
                StudentName = student is null ? "-" : studentUsers.GetValueOrDefault(student.UserId).DisplayName(),
                StudentCellphone = student?.Cellphone,
                WorkerName = c.AssignedTo is int id && workers.TryGetValue(id, out var w) ? w.Name : null,
                Timer = TimerHelper.Compute(c.Deadline, c.StatusId, now)
            };
        }).ToList();
    }

    // ---------------- Students ----------------

    public async Task<List<StudentRow>> GetStudentsAsync()
    {
        var students = (await _client.From<Student>().Order("student_number", Ordering.Ascending).Get()).Models;
        var users = await GetUsersByIdsAsync(students.Select(s => s.UserId));
        return students.Select(s => new StudentRow
        {
            StudentId = s.StudentId,
            Email = s.ContactEmail,
            FullName = users.GetValueOrDefault(s.UserId).DisplayName(),
            StudentNumber = s.StudentNumber,
            RoomNumber = s.RoomNumber,
            Cellphone = s.Cellphone,
            IsRegistered = s.IsRegistered,
            AccountStatus = users.GetValueOrDefault(s.UserId)?.AccountStatus ?? AccountStatuses.Active,
            StatusReason = users.GetValueOrDefault(s.UserId)?.StatusReason
        }).ToList();
    }

    /// <summary>Name, cellphone and whether they have already set a password, for sending an invite.</summary>
    public async Task<(string Name, string Cellphone, bool IsRegistered, string? Email)?> GetStudentContactAsync(int studentId)
    {
        var s = (await _client.From<Student>().Filter("student_id", Operator.Equals, studentId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        if (s is null) return null;
        var user = (await GetUsersByIdsAsync(new[] { s.UserId })).GetValueOrDefault(s.UserId);
        return (user.DisplayName(), s.Cellphone, s.IsRegistered, s.ContactEmail);
    }

    /// <summary>The student row and its user row, for editing.</summary>
    public async Task<(Student Student, AppUser User)?> GetStudentWithUserAsync(int studentId)
    {
        var s = (await _client.From<Student>().Filter("student_id", Operator.Equals, studentId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        if (s is null) return null;
        var u = await GetUserByIdAsync(s.UserId);
        return u is null ? null : (s, u);
    }

    public Task UpdateStudentAsync(Student student) => _client.From<Student>().Update(student);

    /// <summary>True when another student already has this cellphone number (any common way of writing it).</summary>
    public async Task<bool> StudentCellphoneTakenAsync(string cellphone, int exceptStudentId)
    {
        var target = SmsService.ToE164(cellphone);
        var all = (await _client.From<Student>().Get()).Models;
        return all.Any(s => s.StudentId != exceptStudentId && SmsService.ToE164(s.Cellphone) == target);
    }

    public async Task<bool> StudentNumberExistsAsync(string studentNumber)
    {
        var result = await _client.From<Student>()
            .Filter("student_number", Operator.Equals, studentNumber.Trim())
            .Limit(1)
            .Get();
        return result.Models.Count > 0;
    }

    /// <summary>
    /// Creates the users row (role = student) and the linked student row.
    /// The initial password is the student number; the mobile app's first-login flow
    /// should make the student set their own password and flip is_registered to true.
    /// </summary>
    /// <summary>Creates the student and returns their student id.</summary>
    public async Task<int> CreateStudentAsync(AddStudentViewModel model)
    {
        var number = model.StudentNumber.Trim();
        var user = new AppUser
        {
            Email = $"{number}@{_studentEmailDomain}".ToLowerInvariant(),
            // Not the student number: nobody can sign in until First Login (SMS code) sets a real password.
            PasswordHash = PasswordHasher.Hash(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))),
            Role = Roles.Student,
            FullName = model.FullName.Trim()
        };

        var createdUser = (await _client.From<AppUser>().Insert(user)).Model
            ?? throw new InvalidOperationException("Supabase did not return the created user.");

        try
        {
            var created = (await _client.From<Student>().Insert(new Student
            {
                UserId = createdUser.UserId,
                StudentNumber = number,
                RoomNumber = model.RoomNumber.Trim().ToUpperInvariant(),
                Cellphone = model.Cellphone.Trim(),
                ContactEmail = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim().ToLowerInvariant(),
                IsRegistered = false
            })).Model;
            return created?.StudentId ?? 0;
        }
        catch
        {
            // Roll back the orphaned user so the manager can retry.
            await _client.From<AppUser>().Filter("user_id", Operator.Equals, createdUser.UserId.ToString()).Delete();
            throw;
        }
    }

    // ---------------- Workers management ----------------

    public async Task<List<WorkerManagementRow>> GetWorkersForManagementAsync()
    {
        var workers = (await _client.From<InternalWorker>().Get()).Models;
        var users = await GetUsersByIdsAsync(workers.Select(w => w.UserId));
        var allComplaints = (await _client.From<Complaint>()
            .Filter("status_id", Operator.NotEqual, StatusIds.Resolved.ToString())
            .Get()).Models;

        var jobCounts = allComplaints
            .Where(c => c.AssignedTo.HasValue)
            .GroupBy(c => c.AssignedTo!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return workers.Select(w => new WorkerManagementRow
        {
            WorkerId = w.WorkerId,
            Name = users.GetValueOrDefault(w.UserId).DisplayName(),
            Email = users.GetValueOrDefault(w.UserId)?.Email,
            Specialisation = w.Specialisation,
            Phone = w.Phone,
            IsAvailable = w.IsAvailable,
            AccountStatus = users.GetValueOrDefault(w.UserId)?.AccountStatus ?? AccountStatuses.Active,
            StatusReason = users.GetValueOrDefault(w.UserId)?.StatusReason,
            AssignedJobs = jobCounts.GetValueOrDefault(w.WorkerId, 0)
        }).OrderBy(w => w.Name).ToList();
    }

    /// <summary>Creates the worker with a random one-time password and returns it so the manager can pass it on.</summary>
    public async Task<string> CreateWorkerAsync(AddWorkerViewModel model)
    {
        var initialPassword = GenerateInitialPassword();
        var user = new AppUser
        {
            Email = model.Email.Trim().ToLowerInvariant(),
            PasswordHash = PasswordHasher.Hash(initialPassword),
            MustChangePassword = true,
            Cellphone = model.Phone?.Trim(),
            Role = Roles.Worker,
            FullName = model.FullName.Trim()
        };
        var createdUser = (await _client.From<AppUser>().Insert(user)).Model
            ?? throw new InvalidOperationException("Supabase did not return the created user.");
        try
        {
            await _client.From<InternalWorker>().Insert(new InternalWorker
            {
                UserId = createdUser.UserId,
                Specialisation = model.Specialisation?.Trim(),
                Phone = model.Phone?.Trim(),
                IsAvailable = true
            });
        }
        catch
        {
            await _client.From<AppUser>().Filter("user_id", Operator.Equals, createdUser.UserId.ToString()).Delete();
            throw;
        }
        return initialPassword;
    }

    private static string GenerateInitialPassword()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = System.Security.Cryptography.RandomNumberGenerator.GetItems<char>(alphabet, 11);
        return new string(chars) + "7a"; // always has a digit and a lowercase letter
    }

    // ---------------- Suspending, removing and deleting people ----------------

    /// <summary>Takes a worker's open jobs back to the queue (unassigned, waiting for the manager). Returns how many.</summary>
    public async Task<int> ReleaseWorkerJobsAsync(int workerId)
    {
        var open = (await _client.From<Complaint>()
            .Filter("assigned_to", Operator.Equals, workerId.ToString())
            .Filter("status_id", Operator.NotEqual, StatusIds.Resolved.ToString())
            .Filter("status_id", Operator.NotEqual, StatusIds.Rejected.ToString())
            .Get()).Models;
        foreach (var c in open)
        {
            c.AssignedTo = null; c.AssignedType = null; c.AssignedAt = null; c.Deadline = null;
            c.StatusId = StatusIds.Submitted; c.TimerStatus = TimerStatuses.Pending;
            await _client.From<Complaint>().Update(c);
        }
        return open.Count;
    }

    /// <summary>Deletes the auto-assignment rules that point at this worker. Returns how many.</summary>
    public async Task<int> RemoveWorkerRulesAsync(int workerId)
    {
        var rules = (await _client.From<AutoAssignmentRule>().Filter("worker_id", Operator.Equals, workerId.ToString()).Get()).Models;
        foreach (var r in rules) await DeleteRuleAsync(r.RuleId);
        return rules.Count;
    }

    /// <summary>How many requests were ever given to this worker (their history).</summary>
    public async Task<int> CountWorkerHistoryAsync(int workerId) =>
        (await _client.From<Complaint>().Filter("assigned_to", Operator.Equals, workerId.ToString()).Get()).Models.Count;

    /// <summary>How many requests this student ever made (their history).</summary>
    public async Task<int> CountStudentHistoryAsync(int studentId) =>
        (await _client.From<Complaint>().Filter("student_id", Operator.Equals, studentId.ToString()).Get()).Models.Count;

    /// <summary>Stops notifications to a student's devices: deletes their push subscriptions.</summary>
    public Task DeleteStudentPushAsync(int studentId) =>
        _client.From<PushSubscriptionRow>().Filter("student_id", Operator.Equals, studentId.ToString()).Delete();

    /// <summary>Deletes a worker and their account for good. Callers check there is no history first.</summary>
    public async Task DeleteWorkerAsync(InternalWorker worker)
    {
        await RemoveWorkerRulesAsync(worker.WorkerId);
        await _client.From<InternalWorker>().Filter("worker_id", Operator.Equals, worker.WorkerId.ToString()).Delete();
        await _client.From<AppUser>().Filter("user_id", Operator.Equals, worker.UserId.ToString()).Delete();
    }

    /// <summary>Deletes a student and their account for good. Callers check there is no history first (deleting a student would also delete their requests).</summary>
    public async Task DeleteStudentAsync(Student student)
    {
        var id = student.StudentId.ToString();
        await _client.From<StudentNotification>().Filter("student_id", Operator.Equals, id).Delete();
        await _client.From<PushSubscriptionRow>().Filter("student_id", Operator.Equals, id).Delete();
        await _client.From<StudentOtp>().Filter("cellphone", Operator.Equals, student.Cellphone).Delete();
        await _client.From<Student>().Filter("student_id", Operator.Equals, id).Delete();
        await _client.From<AppUser>().Filter("user_id", Operator.Equals, student.UserId.ToString()).Delete();
    }

    // ---------------- External company ----------------

    public async Task<ExternalCompany?> GetExternalCompanyAsync()
    {
        var result = await _client.From<ExternalCompany>().Limit(1).Get();
        return result.Models.FirstOrDefault();
    }

    public async Task SaveExternalCompanyAsync(ExternalCompanyViewModel model)
    {
        var existing = await GetExternalCompanyAsync();
        if (existing is null)
        {
            await _client.From<ExternalCompany>().Insert(new ExternalCompany
            {
                CompanyName = model.CompanyName.Trim(),
                ApiEndpoint = string.IsNullOrWhiteSpace(model.ApiEndpoint) ? null : model.ApiEndpoint.Trim(),
                ContactEmail = model.ContactEmail.Trim(),
                ContactPhone = model.ContactPhone.Trim(),
                IsActive = model.IsActive
            });
        }
        else
        {
            existing.CompanyName = model.CompanyName.Trim();
            existing.ApiEndpoint = string.IsNullOrWhiteSpace(model.ApiEndpoint) ? null : model.ApiEndpoint.Trim();
            existing.ContactEmail = model.ContactEmail.Trim();
            existing.ContactPhone = model.ContactPhone.Trim();
            existing.IsActive = model.IsActive;
            await _client.From<ExternalCompany>().Update(existing);
        }
    }

    // ---------------- Categories management ----------------

    public async Task AddCategoryAsync(string name)
    {
        var existing = (await _client.From<Category>()
            .Filter("name", Operator.ILike, name.Trim())
            .Limit(1).Get()).Models;
        if (existing.Count > 0) return;
        await _client.From<Category>().Insert(new Category { Name = name.Trim() });
    }

    public async Task DeleteCategoryAsync(int categoryId)
    {
        var id = categoryId.ToString();
        // A complaint can point at the category directly, or remember it as the one the AI changed it from.
        var used = (await _client.From<Complaint>().Filter("category_id", Operator.Equals, id).Limit(1).Get()).Models.Count > 0
            || (await _client.From<Complaint>().Filter("original_category_id", Operator.Equals, id).Limit(1).Get()).Models.Count > 0;
        if (used) throw new InvalidOperationException("Cannot delete a category that has complaints.");
        var hasRule = (await _client.From<AutoAssignmentRule>().Filter("category_id", Operator.Equals, id).Limit(1).Get()).Models.Count > 0;
        if (hasRule) throw new InvalidOperationException("Remove this category's auto-assignment rule first.");
        await _client.From<Category>().Filter("category_id", Operator.Equals, id).Delete();
    }

    // ---------------- Auto-assignment rules ----------------

    public async Task<List<RuleRow>> GetRulesAsync()
    {
        var rules = (await _client.From<AutoAssignmentRule>().Get()).Models;
        var categories = (await GetCategoriesAsync()).ToDictionary(c => c.CategoryId, c => c.Name);
        var workers = (await GetWorkersAsync()).ToDictionary(w => w.WorkerId);

        return rules.Select(r => new RuleRow
        {
            RuleId = r.RuleId,
            CategoryName = categories.GetValueOrDefault(r.CategoryId, "Unknown"),
            WorkerName = workers.TryGetValue(r.WorkerId, out var w) ? w.Name : "Unknown",
            Specialisation = workers.TryGetValue(r.WorkerId, out var ws) ? ws.Specialisation : null
        }).OrderBy(r => r.CategoryName).ToList();
    }

    /// <summary>One rule per category: updates the existing rule or creates a new one.</summary>
    /// <summary>Saves the rule; false when the category or the worker does not exist.</summary>
    public async Task<bool> SaveRuleAsync(int categoryId, int workerId)
    {
        if (!(await GetCategoriesAsync()).Any(c => c.CategoryId == categoryId)) return false;
        if (!await IsWorkerActiveAsync(workerId)) return false;
        var existing = (await _client.From<AutoAssignmentRule>()
            .Filter("category_id", Operator.Equals, categoryId.ToString())
            .Limit(1)
            .Get()).Models.FirstOrDefault();

        if (existing is null)
        {
            await _client.From<AutoAssignmentRule>().Insert(new AutoAssignmentRule { CategoryId = categoryId, WorkerId = workerId });
        }
        else
        {
            existing.WorkerId = workerId;
            await _client.From<AutoAssignmentRule>().Update(existing);
        }
        return true;
    }

    public Task DeleteRuleAsync(int ruleId) =>
        _client.From<AutoAssignmentRule>().Filter("rule_id", Operator.Equals, ruleId.ToString()).Delete();
}
