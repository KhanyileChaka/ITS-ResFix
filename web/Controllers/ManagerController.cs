using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.ViewModels;
using ITSResFix.Staff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ITSResFix.Staff.Controllers;

[Authorize(Roles = Roles.Manager)]
public class ManagerController : Controller
{
    private readonly ResFixDataService _data;
    private readonly ILogger<ManagerController> _logger;
    private readonly EmailService _email;
    private readonly PasswordResetService _welcome;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly SmsService _sms;
    private readonly ChatService _chat;

    public ManagerController(ResFixDataService data, ILogger<ManagerController> logger, EmailService email,
        PasswordResetService welcome, IConfiguration config, IWebHostEnvironment env, SmsService sms, ChatService chat)
    {
        _chat = chat;
        _sms = sms;
        _data = data;
        _logger = logger;
        _email = email;
        _welcome = welcome;
        _config = config;
        _env = env;
    }

    // ---------- Dashboard ----------

    public async Task<IActionResult> Dashboard()
    {
        await _data.SyncExpiredTimersAsync();
        var complaints = await _data.GetComplaintsAsync();
        var workers = await _data.GetWorkersAsync();
        bool IsOpen(ComplaintRow r) => r.Complaint.StatusId is StatusIds.Submitted or StatusIds.InProgress;

        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, DisplayHelper.SaZone).Date;
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).ToList();
        var perDay = days.Select(d => complaints.Count(r =>
            TimeZoneInfo.ConvertTimeFromUtc(r.Complaint.CreatedAt.AsUtc(), DisplayHelper.SaZone).Date == d)).ToList();

        return View(new ManagerDashboardViewModel
        {
            Total = complaints.Count(c => c.Complaint.StatusId != StatusIds.Rejected),
            Submitted = complaints.Count(c => c.Complaint.StatusId == StatusIds.Submitted),
            InProgress = complaints.Count(c => c.Complaint.StatusId == StatusIds.InProgress),
            Resolved = complaints.Count(c => c.Complaint.StatusId == StatusIds.Resolved),
            Rejected = complaints.Count(c => c.Complaint.StatusId == StatusIds.Rejected),
            Emergencies = complaints.Count(r => IsOpen(r) && r.IsEmergency),
            Overdue = complaints.Count(r => IsOpen(r) && r.Timer.IsExpired),
            Unassigned = complaints.Count(r => IsOpen(r) && r.Complaint.AssignedTo is null),
            Appeals = complaints.Count(r => r.AppealPending),
            AwaitingReply = complaints.Count(r => IsOpen(r) && r.AwaitingReply),
            OnTimeRate = DisplayHelper.OnTimeRate(complaints.Select(r => r.Complaint)),
            Days = days,
            PerDay = perDay,
            Workers = workers
                .Select(w => new WorkerLoad(w.Name, w.IsAvailable,
                    complaints.Count(r => IsOpen(r) && r.Complaint.AssignedTo == w.WorkerId),
                    complaints.Count(r => IsOpen(r) && r.Complaint.AssignedTo == w.WorkerId && r.Timer.IsExpired)))
                .OrderByDescending(w => w.Open).ThenBy(w => w.Name).Take(6).ToList(),
            Recent = complaints.Take(8).ToList()
        });
    }

    // ---------- Complaints ----------

    public async Task<IActionResult> Complaints(bool emergency = false, int? status = null,
        string? search = null, int? category = null, string? timerStatus = null, string? priority = null)
    {
        var everything = await _data.GetComplaintsAsync(emergency, null);
        var all = await FilterComplaintsAsync(emergency, status, search, category, timerStatus, priority);

        return View(new ComplaintListViewModel
        {
            Complaints = all,
            EmergencyOnly = emergency,
            StatusFilter = status,
            SearchQuery = search,
            CategoryFilter = category,
            TimerStatusFilter = timerStatus,
            PriorityFilter = priority,
            Categories = await _data.GetCategoriesAsync(),
            CountAll = everything.Count,
            CountSubmitted = everything.Count(r => r.Complaint.StatusId == StatusIds.Submitted),
            CountInProgress = everything.Count(r => r.Complaint.StatusId == StatusIds.InProgress),
            CountResolved = everything.Count(r => r.Complaint.StatusId == StatusIds.Resolved),
            CountRejected = everything.Count(r => r.Complaint.StatusId == StatusIds.Rejected)
        });
    }

    /// <summary>The complaints list as a spreadsheet, using the same filters as the page.</summary>
    public async Task<IActionResult> ExportComplaints(bool emergency = false, int? status = null,
        string? search = null, int? category = null, string? timerStatus = null, string? priority = null)
    {
        var rows = await FilterComplaintsAsync(emergency, status, search, category, timerStatus, priority);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Job,Created,Student,Student number,Room,Category,Status,Priority,Assigned to,Deadline,Resolved");
        foreach (var r in rows)
        {
            var c = r.Complaint;
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(DisplayHelper.JobCode(c.ComplaintId)), Csv(((DateTime?)c.CreatedAt).ToSaTime("yyyy-MM-dd HH:mm")),
                Csv(r.StudentName), Csv(r.StudentNumber), Csv(c.RoomNumber), Csv(r.CategoryName), Csv(r.StatusName), Csv(c.Priority),
                Csv(r.WorkerName ?? ""), Csv(c.Deadline.ToSaTime("yyyy-MM-dd HH:mm")), Csv(c.ResolvedAt.ToSaTime("yyyy-MM-dd HH:mm"))
            }));
        }
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        Response.Headers.CacheControl = "no-store"; // personal data: keep it out of shared caches
        return File(bytes, "text/csv", $"complaints-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>Quotes a CSV cell; a leading = + - @ is neutralised so spreadsheets never run it as a formula.</summary>
    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private async Task<List<ComplaintRow>> FilterComplaintsAsync(bool emergency, int? status, string? search,
        int? category, string? timerStatus, string? priority)
    {
        var all = await _data.GetComplaintsAsync(emergency, status);
        if (!string.IsNullOrWhiteSpace(priority))
            all = all.Where(r => string.Equals(r.Complaint.Priority, priority, StringComparison.OrdinalIgnoreCase)).ToList();

        // Apply search filter in-memory
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            all = all.Where(r =>
                r.Complaint.ComplaintId.ToString().Contains(q) ||
                r.StudentName.ToLowerInvariant().Contains(q) ||
                r.StudentNumber.ToLowerInvariant().Contains(q) ||
                r.Complaint.RoomNumber.ToLowerInvariant().Contains(q)
            ).ToList();
        }
        if (category.HasValue)
            all = all.Where(r => r.Complaint.CategoryId == category.Value).ToList();
        if (!string.IsNullOrWhiteSpace(timerStatus))
            all = all.Where(r => r.Timer.CssClass == timerStatus).ToList();
        return all;
    }

    // ---------- Complaint detail ----------

    public async Task<IActionResult> ComplaintDetail(int id)
    {
        var row = await _data.GetComplaintRowAsync(id);
        if (row is null) return NotFound();
        ViewBag.Messages = await _data.GetMessagesAsync(id);
        return View(row);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PostMessage(int id, string? body)
    {
        var sent = await _data.PostStaffMessageAsync(id, User.Identity?.Name ?? "Residence manager", body ?? "");
        if (sent) _chat.StopTyping(id, ChatService.Staff);
        if (Request.Headers["X-Requested-With"] == "fetch")
            return Json(new { ok = sent, error = sent ? null : "The message could not be sent (it is empty, or the request was rejected)." });
        if (sent) TempData["Success"] = "Message sent to the student.";
        else TempData["Error"] = "The message could not be sent (it is empty, or the request was rejected).";
        return RedirectToAction(nameof(ComplaintDetail), new { id });
    }

    /// <summary>The conversation for the open complaint page (polled every few seconds). seen=true means the page is on screen.</summary>
    [HttpGet]
    public async Task<IActionResult> ComplaintChat(int id, bool seen = false)
    {
        if (await _data.GetComplaintAsync(id) is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Json(await _chat.ThreadAsync(id, ChatService.Staff, seen));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ComplaintTyping(int id)
    {
        if (!_chat.AllowPing(id, ChatService.Staff)) return Ok();
        if (await _data.GetComplaintAsync(id) is null) return NotFound();
        _chat.Typing(id, ChatService.Staff, User.Identity?.Name ?? "Residence manager");
        return Ok();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideAppeal(int id, bool approve, string? response)
    {
        if (response?.Length > 500) response = response[..500];
        var ok = approve ? await _data.AcceptRejectedAsync(id, response) is not null : await _data.DeclineAppealAsync(id, response);
        if (ok) TempData["Success"] = approve ? $"Appeal approved: request C{id:D3} is back in the queue. The student was told." : $"Appeal declined. The student was told.";
        else TempData["Error"] = "There is no pending appeal on that request.";
        return RedirectToAction(nameof(ComplaintDetail), new { id });
    }

    // ---------- Expired complaints ----------

    public async Task<IActionResult> ExpiredComplaints()
    {
        await _data.SyncExpiredTimersAsync();
        return View(new ExpiredComplaintsViewModel
        {
            Complaints = await _data.GetExpiredComplaintsAsync(),
            Workers = await _data.GetWorkersAsync()
        });
    }

    // ---------- Reassign (new dedicated page) ----------

    [HttpGet]
    public async Task<IActionResult> Reassign(int id)
    {
        var row = await _data.GetComplaintRowAsync(id);
        if (row is null) return NotFound();
        var company = await _data.GetExternalCompanyAsync();
        return View(new ReassignViewModel
        {
            Row = row,
            Workers = (await _data.GetWorkersForManagementAsync()).Where(w => w.AccountStatus == AccountStatuses.Active).ToList(),
            ExternalCompanyName = company?.CompanyName,
            ExternalContactEmail = company?.ContactEmail,
            ExternalContactPhone = company?.ContactPhone
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptRejected(int id)
    {
        var worker = await _data.AcceptRejectedAsync(id);
        if (worker is null)
            TempData["Error"] = "That complaint is not in a rejected state.";
        else
            TempData["Success"] = worker.Length > 0
                ? $"Complaint #C{id:D3} accepted and assigned to {worker}. The student has been notified."
                : $"Complaint #C{id:D3} accepted. It has no auto-assignment rule, so it is waiting to be assigned. The student has been notified.";
        return RedirectToAction(nameof(ComplaintDetail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DoReassign(int complaintId, int? workerId, bool useExternal, string? reason)
    {
        if (!useExternal && (!workerId.HasValue || workerId.Value == 0))
        {
            TempData["Error"] = "Please select a worker or the external company.";
            return RedirectToAction(nameof(Reassign), new { id = complaintId });
        }

        if (useExternal)
        {
            var ext = await _data.ReassignToExternalAsync(complaintId);
            if (!ext.Ok) TempData["Error"] = $"Complaint #C{complaintId:D3} could not be escalated (it is missing, resolved or rejected).";
            else if (ext.Emailed) TempData["Success"] = $"Complaint #C{complaintId:D3} escalated to {ext.CompanyName}. The details were emailed to {ext.ContactEmail}.";
            else TempData["Success"] = $"Complaint #C{complaintId:D3} escalated to the external company. No email was sent ({(_email.IsConfigured ? "the company has no active contact email" : "email is not set up on the server")}), so contact them yourself" + (string.IsNullOrWhiteSpace(ext.ContactPhone) ? "." : $" on {ext.ContactPhone}.");
        }
        else
        {
            var workers = await _data.GetWorkersAsync();
            var worker = workers.FirstOrDefault(w => w.WorkerId == workerId);
            if (worker is null)
            {
                TempData["Error"] = "Selected worker not found.";
                return RedirectToAction(nameof(Reassign), new { id = complaintId });
            }
            if (await _data.ReassignAsync(complaintId, workerId!.Value))
                TempData["Success"] = $"Complaint #{complaintId} reassigned to {worker.Name}. A new 48-hour timer has started.";
            else
                TempData["Error"] = $"Complaint #{complaintId} could not be reassigned.";
        }
        return RedirectToAction(nameof(ExpiredComplaints));
    }

    // ---------- Students ----------

    public async Task<IActionResult> Students(string? search = null, string? regStatus = null, bool add = false)
    {
        ViewBag.OpenAdd = add;
        ViewBag.AddStudentModel = new AddStudentViewModel();
        return await StudentsViewAsync(search, regStatus);
    }

    private async Task<IActionResult> StudentsViewAsync(string? search, string? regStatus)
    {
        var students = await _data.GetStudentsAsync();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            students = students.Where(s =>
                s.FullName.ToLowerInvariant().Contains(q) ||
                s.StudentNumber.ToLowerInvariant().Contains(q)
            ).ToList();
        }
        ViewBag.RemovedCount = students.Count(s => s.AccountStatus == AccountStatuses.Removed);
        ViewBag.SuspendedCount = students.Count(s => s.AccountStatus == AccountStatuses.Suspended);
        // removed people only show when asked for; "registered" and "pending" mean people who can use the app
        if (regStatus == "removed") students = students.Where(s => s.AccountStatus == AccountStatuses.Removed).ToList();
        else if (regStatus == "suspended") students = students.Where(s => s.AccountStatus == AccountStatuses.Suspended).ToList();
        else
        {
            students = students.Where(s => s.AccountStatus != AccountStatuses.Removed).ToList();
            if (regStatus == "registered") students = students.Where(s => s.IsRegistered).ToList();
            else if (regStatus == "pending") students = students.Where(s => !s.IsRegistered).ToList();
        }

        ViewData["Search"] = search;
        ViewData["RegStatus"] = regStatus ?? "all";
        return View("Students", students);
    }

    /// <summary>Adding a student now happens in a dialog on the Students page; old links land there with it open.</summary>
    [HttpGet]
    public IActionResult AddStudent() => RedirectToAction(nameof(Students), new { add = true });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddStudent(AddStudentViewModel model)
    {
        async Task<IActionResult> Again() { ViewBag.OpenAdd = true; ViewBag.AddStudentModel = model; return await StudentsViewAsync(null, null); }
        if (!ModelState.IsValid) return await Again();
        if (await _data.StudentNumberExistsAsync(model.StudentNumber))
        {
            ModelState.AddModelError(nameof(model.StudentNumber), "A student with this number is already registered.");
            return await Again();
        }
        int newStudentId;
        try
        {
            newStudentId = await _data.CreateStudentAsync(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create student {StudentNumber}", model.StudentNumber);
            ModelState.AddModelError(string.Empty, "The student could not be saved. Please try again.");
            return await Again();
        }

        var added = $"{model.FullName.Trim()} ({model.StudentNumber.Trim()}) was added.";
        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            // an email costs nothing, so the invite goes out straight away; the SMS version is sent from the Send invite button
            var sentEmail = await SendStudentInviteEmailAsync(model.FullName.Trim(), model.Email.Trim(), model.Cellphone.Trim());
            added += sentEmail ? " An invite email was sent." : " No invite email could be sent (use Send invite to try again).";
        }
        else added += " Use Send invite to tell them how to get started.";
        TempData["Success"] = added;
        return RedirectToAction(nameof(Students));
    }

    // ---------- Workers ----------

    public async Task<IActionResult> Workers(bool add = false, bool removed = false)
    {
        ViewBag.OpenAdd = add;
        ViewBag.AddWorkerModel = new AddWorkerViewModel();
        return await WorkersViewAsync(removed);
    }

    /// <summary>The kinds of work a worker can specialise in: one per category, plus "General maintenance" (empty).</summary>
    private async Task<List<string>> SpecialisationsAsync() => (await _data.GetCategoriesAsync()).Select(c => c.Name).ToList();

    private async Task<IActionResult> WorkersViewAsync(bool showRemoved = false)
    {
        ViewBag.Specialisations = await SpecialisationsAsync();
        var all = await _data.GetWorkersForManagementAsync();
        ViewBag.RemovedCount = all.Count(w => w.AccountStatus == AccountStatuses.Removed);
        ViewBag.ShowRemoved = showRemoved;
        return View("Workers", new WorkersManagementViewModel { Workers = all.Where(w => showRemoved ? w.AccountStatus == AccountStatuses.Removed : w.AccountStatus != AccountStatuses.Removed).ToList() });
    }

    /// <summary>Adding a worker now happens in a dialog on the Workers page; old links land there with it open.</summary>
    [HttpGet]
    public IActionResult AddWorker() => RedirectToAction(nameof(Workers), new { add = true });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddWorker(AddWorkerViewModel model)
    {
        async Task<IActionResult> Again() { ViewBag.OpenAdd = true; ViewBag.AddWorkerModel = model; return await WorkersViewAsync(); }

        // the specialisation comes from a list; refuse anything that is not one of the categories
        if (!string.IsNullOrWhiteSpace(model.Specialisation) && !(await SpecialisationsAsync()).Contains(model.Specialisation.Trim()))
            ModelState.AddModelError(nameof(model.Specialisation), "Choose one of the listed specialisations.");
        if (!ModelState.IsValid) return await Again();
        try
        {
            var initialPassword = await _data.CreateWorkerAsync(model);

            // Tell the new worker: an email with a "choose your password" link, plus an SMS.
            var baseUrl = PublicBaseUrl();
            var welcome = new PasswordResetService.WelcomeResult(false, false, "the site address is not set up (Hosting:PublicUrl)");
            var created = await _data.GetUserForLoginAsync(model.Email);
            if (created is not null && baseUrl is not null)
                welcome = await _welcome.WelcomeWorkerAsync(created, model.Phone, baseUrl);

            var told = DescribeWelcome(welcome, model.Phone);
            TempData["Success"] = $"{model.FullName.Trim()} was added as a worker. {told} Their one-time password, in case you need to pass it on yourself, is {initialPassword} (shown only now).";
            return RedirectToAction(nameof(Workers));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create worker {Email}", model.Email);
            ModelState.AddModelError(string.Empty, "The worker could not be saved. The email may already exist.");
            return await Again();
        }
    }

    // ---------- Resend welcome / invite ----------

    private static readonly Dictionary<string, List<DateTime>> InviteLog = new();

    /// <summary>At most 3 sends per person per hour, so these buttons cannot be used to flood anyone with messages.</summary>
    private static bool InviteAllowed(string key)
    {
        lock (InviteLog)
        {
            var list = InviteLog.TryGetValue(key, out var l) ? l : InviteLog[key] = new List<DateTime>();
            list.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromHours(1));
            if (list.Count >= 3) return false;
            list.Add(DateTime.UtcNow);
            return true;
        }
    }

    private string? PublicBaseUrl()
    {
        var url = _config["Hosting:PublicUrl"];
        if (string.IsNullOrWhiteSpace(url) && _env.IsDevelopment()) url = $"{Request.Scheme}://{Request.Host}";
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }

    private string DescribeWelcome(PasswordResetService.WelcomeResult w, string? phone) =>
        (w.Emailed ? "A welcome email was sent" : $"No email was sent ({w.Note})")
        + (w.Texted ? " and an SMS was sent." : ". No SMS was sent" + (string.IsNullOrWhiteSpace(phone) ? " (no phone number)." : " (SMS is not set up, or the SMS account is out of credits)."));

    /// <summary>Sends a worker a fresh welcome email (with a new "choose your password" link) and SMS. Earlier links stop working.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendWorkerWelcome(int workerId)
    {
        var worker = await _data.GetWorkerAsync(workerId);
        var user = worker is null ? null : await _data.GetUserByIdAsync(worker.UserId);
        if (worker is null || user is null) return NotFound();
        if (user.AccountStatus != AccountStatuses.Active)
        {
            TempData["Error"] = $"{user.DisplayName()} is {user.AccountStatus}. Restore them first.";
            return RedirectToAction(nameof(Workers));
        }

        if (!InviteAllowed($"worker-{workerId}"))
        {
            TempData["Error"] = $"{user.DisplayName()} has already been sent 3 messages in the last hour. Try again later.";
            return RedirectToAction(nameof(Workers));
        }
        var baseUrl = PublicBaseUrl();
        if (baseUrl is null) { TempData["Error"] = "The site address is not set up (Hosting:PublicUrl), so no link can be sent."; return RedirectToAction(nameof(Workers)); }

        var result = await _welcome.WelcomeWorkerAsync(user, worker.Phone, baseUrl);
        var text = DescribeWelcome(result, worker.Phone);
        if (result.Emailed || result.Texted) TempData["Success"] = $"{user.DisplayName()}: {text} Any earlier link no longer works.";
        else TempData["Error"] = $"Nothing could be sent to {user.DisplayName()}. {text}";
        return RedirectToAction(nameof(Workers));
    }

    private string StudentAppUrl() => string.IsNullOrWhiteSpace(_config["Hosting:StudentAppUrl"]) ? "https://student.itsdance.co.za" : _config["Hosting:StudentAppUrl"]!;

    private async Task<bool> SendStudentInviteEmailAsync(string name, string email, string cellphone)
    {
        if (!_email.IsConfigured) return false;
        var (html, text) = EmailTemplates.StudentInvite(name, StudentAppUrl(), cellphone);
        return await _email.SendAsync(email, "Your ITS ResFix account is ready", text, html);
    }

    /// <summary>Tells a student how to get started, by email when they have one and by SMS: open the app, choose First Login, use their number.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendStudentInvite(int studentId)
    {
        var student = await _data.GetStudentContactAsync(studentId);
        if (student is null) return NotFound();
        var (name, cellphone, registered, email) = student.Value;

        if (registered) { TempData["Error"] = $"{name} has already set up their account."; return RedirectToAction(nameof(Students)); }
        if ((await _data.GetStudentWithUserAsync(studentId))?.User.AccountStatus is { } st && st != AccountStatuses.Active)
        { TempData["Error"] = $"{name} is {st}. Restore them first."; return RedirectToAction(nameof(Students)); }
        var canEmail = !string.IsNullOrWhiteSpace(email) && _email.IsConfigured;
        if (!canEmail && !_sms.IsConfigured)
        {
            TempData["Error"] = string.IsNullOrWhiteSpace(email)
                ? $"{name} has no email address saved and SMS is not set up. Add an email with Edit, then try again."
                : "Neither email nor SMS is set up on the server, so no invite can be sent.";
            return RedirectToAction(nameof(Students));
        }
        if (!InviteAllowed($"student-{studentId}"))
        {
            TempData["Error"] = $"{name} has already been sent 3 invites in the last hour. Try again later.";
            return RedirectToAction(nameof(Students));
        }

        var emailed = canEmail && await SendStudentInviteEmailAsync(name, email!, cellphone);
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        var texted = _sms.IsConfigured && await _sms.SendAsync(cellphone, $"ITS ResFix: hi {first}, your account is ready. Open {StudentAppUrl()}, choose First Login and enter this cellphone number to set your password.");

        var parts = new List<string>();
        if (emailed) parts.Add($"an email to {email}");
        if (texted) parts.Add("an SMS");
        if (parts.Count > 0) TempData["Success"] = $"Invite sent to {name}: {string.Join(" and ", parts)}.";
        else TempData["Error"] = $"Nothing could be sent to {name}. " + (string.IsNullOrWhiteSpace(email) ? "They have no email saved, and the SMS failed (check the number and your SMS credits)." : "The email and the SMS both failed (check the address, the number and your SMS credits).");
        return RedirectToAction(nameof(Students));
    }

    // ---------- Edit and revoke ----------

    /// <summary>
    /// The levels of taking someone out, like in real life:
    ///   suspend (blocked for now, restored exactly as they were),
    ///   remove (they have left: signed out, no new work, history kept, must be set up again if restored),
    ///   delete (only a removed person with no history at all, and only after typing their name).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeAccess(string kind, int id, string action, string? reason, bool releaseJobs = false, string? confirmName = null)
    {
        var worker = kind == "worker" ? await _data.GetWorkerAsync(id) : null;
        var found = kind == "student" ? await _data.GetStudentWithUserAsync(id) : null;
        var user = worker is not null ? await _data.GetUserByIdAsync(worker.UserId) : found?.User;
        var student = found?.Student;
        var back = kind == "worker" ? nameof(Workers) : nameof(Students);
        if (user is null || (kind == "worker" && worker is null) || (kind == "student" && student is null)) return NotFound();
        if (user.Role is not (Roles.Worker or Roles.Student)) return NotFound();

        var name = user.DisplayName();
        var by = User.Identity?.Name ?? "Manager";
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is { Length: > 200 }) reason = reason[..200];

        void Mark(string status)
        {
            user.AccountStatus = status;
            user.StatusReason = status == AccountStatuses.Active ? null : reason;
            user.StatusChangedAt = DateTime.UtcNow;
            user.StatusChangedBy = by;
        }

        switch (action)
        {
            case "suspend":
            case "remove":
            {
                var removing = action == "remove";
                if (user.AccountStatus == AccountStatuses.Removed || (!removing && user.AccountStatus == AccountStatuses.Suspended))
                    return RedirectToAction(back).WithError(this, $"{name} is already {user.AccountStatus}.");
                Mark(removing ? AccountStatuses.Removed : AccountStatuses.Suspended);
                var done = new List<string>();
                if (worker is not null)
                {
                    worker.IsAvailable = false;
                    await _data.UpdateWorkerAsync(worker);
                    if (releaseJobs)
                    {
                        var n = await _data.ReleaseWorkerJobsAsync(worker.WorkerId);
                        done.Add(n == 0 ? "they had no open jobs" : $"{n} open job{(n == 1 ? "" : "s")} went back to the queue");
                    }
                    if (removing)
                    {
                        var rules = await _data.RemoveWorkerRulesAsync(worker.WorkerId);
                        if (rules > 0) done.Add($"{rules} auto-assignment rule{(rules == 1 ? "" : "s")} removed, so pick someone else for those categories");
                        await _welcome.RevokeAccessAsync(user); // saves the status too; unused links die and the password is wiped
                    }
                    else await _data.UpdateUserAsync(user);
                }
                else if (removing)
                {
                    student!.SmsAlerts = false;
                    await _data.DeleteStudentPushAsync(student.StudentId);
                    await _data.UpdateStudentAsync(student!);
                    await _welcome.CancelStudentSignupAsync(student!, user); // saves the status too; password wiped, must set up again
                }
                else await _data.UpdateUserAsync(user);
                await _welcome.EndSessionsAsync(user.UserId);
                TempData["Success"] = (removing ? $"{name} was removed" : $"{name} was suspended") + ": they were signed out and cannot sign in"
                    + (removing ? ". Their history is kept." : " until you restore them.")
                    + (done.Count > 0 ? " " + string.Join("; ", done) + "." : "");
                break;
            }
            case "restore":
            {
                if (user.AccountStatus == AccountStatuses.Active) return RedirectToAction(back).WithError(this, $"{name} is already active.");
                var wasRemoved = user.AccountStatus == AccountStatuses.Removed;
                Mark(AccountStatuses.Active);
                await _data.UpdateUserAsync(user);
                await _welcome.EndSessionsAsync(user.UserId);
                if (worker is not null && wasRemoved)
                {
                    var baseUrl = PublicBaseUrl();
                    var sent = baseUrl is null ? null : await _welcome.WelcomeWorkerAsync(user, worker.Phone, baseUrl);
                    TempData["Success"] = $"{name} is active again. " + (sent is null ? "No welcome could be sent (the site address is not set up); use Resend welcome."
                        : DescribeWelcome(sent, worker.Phone) + " They must choose a new password. They are off duty until they switch availability on.");
                }
                else if (student is not null && wasRemoved)
                    TempData["Success"] = $"{name} is active again. Their account was reset, so press Send invite and they set up with First Login.";
                else TempData["Success"] = $"{name} is active again and can sign in as before.";
                break;
            }
            case "delete":
            {
                if (user.AccountStatus != AccountStatuses.Removed) return RedirectToAction(back).WithError(this, "Remove the person first. Only someone who has been removed can be deleted for good.");
                if (!string.Equals((confirmName ?? "").Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction(back).WithError(this, "The name you typed does not match, so nothing was deleted.");
                var history = worker is not null ? await _data.CountWorkerHistoryAsync(worker.WorkerId) : await _data.CountStudentHistoryAsync(student!.StudentId);
                if (history > 0)
                    return RedirectToAction(back).WithError(this, $"{name} has {history} request{(history == 1 ? "" : "s")} on record, so they cannot be deleted. Deleting would erase that history. Keep them as removed.");
                if (worker is not null) await _data.DeleteWorkerAsync(worker); else await _data.DeleteStudentAsync(student!);
                await _welcome.EndSessionsAsync(user.UserId);
                TempData["Success"] = $"{name} was deleted for good.";
                break;
            }
            default:
                return BadRequest();
        }
        return RedirectToAction(back);
    }

    private static bool ValidEmail(string? email) =>
        !string.IsNullOrEmpty(email) && email.Length <= 120 &&
        System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@<>""',;()\[\]\x5C]+@[^\s@<>""',;()\[\]\x5C]+\.[^\s@<>""',;()\[\]\x5C]+$");

    /// <summary>
    /// Corrects a worker's details. A changed email means the old one may belong to the wrong person, so every unused link and
    /// any session made with the old address is cancelled and a new welcome goes to the new address.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditWorker(int workerId, string? fullName, string? email, string? phone, string? specialisation)
    {
        var worker = await _data.GetWorkerAsync(workerId);
        var user = worker is null ? null : await _data.GetUserByIdAsync(worker.UserId);
        if (worker is null || user is null) return NotFound();

        fullName = fullName?.Trim(); email = email?.Trim().ToLowerInvariant(); phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(); specialisation = string.IsNullOrWhiteSpace(specialisation) ? null : specialisation.Trim();
        string? problem = null;
        if (string.IsNullOrEmpty(fullName) || fullName.Length > 120) problem = "Enter the worker's full name.";
        else if (!ValidEmail(email)) problem = "Enter a valid email address, like name@example.com.";
        else if (phone is not null && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^(\+27|0)[\d ]{9,12}$")) problem = "Enter a valid SA phone number, or leave it empty.";
        else if (specialisation is not null && !(await SpecialisationsAsync()).Contains(specialisation)) problem = "Choose one of the listed specialisations.";
        if (problem is not null) { TempData["Error"] = problem; return RedirectToAction(nameof(Workers)); }

        var emailChanged = !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase);
        if (emailChanged && await _data.GetUserForLoginAsync(email!) is not null)
        {
            TempData["Error"] = $"{email} already belongs to another account.";
            return RedirectToAction(nameof(Workers));
        }

        user.FullName = fullName!; user.Cellphone = phone; worker.Phone = phone; worker.Specialisation = specialisation;
        if (emailChanged) user.Email = email!;
        await _data.UpdateWorkerAsync(worker);

        if (!emailChanged)
        {
            await _data.UpdateUserAsync(user);
            TempData["Success"] = $"{user.DisplayName()}'s details were saved.";
            return RedirectToAction(nameof(Workers));
        }

        await _welcome.RevokeAccessAsync(user); // saves the new email too
        var msg = $"{user.DisplayName()}'s email is now {email}. The link sent to the old address and any sign-in made with it were cancelled.";
        var baseUrl = PublicBaseUrl();
        if (user.AccountStatus != AccountStatuses.Active) msg += $" No new welcome was sent because they are {user.AccountStatus}.";
        else if (baseUrl is null) msg += " No new welcome could be sent (the site address is not set up).";
        else if (!InviteAllowed($"worker-{workerId}")) msg += " No new welcome was sent: 3 messages already went out in the last hour. Use Resend welcome later.";
        else msg += " " + DescribeWelcome(await _welcome.WelcomeWorkerAsync(user, phone, baseUrl), phone);
        TempData["Success"] = msg;
        return RedirectToAction(nameof(Workers));
    }

    /// <summary>Cancels a worker's links and sessions without changing anything else (an email went to the wrong person). They cannot sign in until a new welcome is sent.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeWorkerAccess(int workerId)
    {
        var worker = await _data.GetWorkerAsync(workerId);
        var user = worker is null ? null : await _data.GetUserByIdAsync(worker.UserId);
        if (worker is null || user is null) return NotFound();
        await _welcome.RevokeAccessAsync(user);
        TempData["Success"] = $"{user.DisplayName()}'s access was revoked: every link sent so far is dead and anyone signed in was signed out. Use Resend welcome email when you are ready to let them in.";
        return RedirectToAction(nameof(Workers));
    }

    /// <summary>
    /// Corrects a student's details. A new cellphone number or email, or the tick-box, cancels an existing sign-up so a wrong person
    /// who got in first is signed out and the real student sets up again; the new address or number is told how to start.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditStudent(int studentId, string? fullName, string? roomNumber, string? cellphone, string? email, bool cancelSignup = false)
    {
        var found = await _data.GetStudentWithUserAsync(studentId);
        if (found is null) return NotFound();
        var (student, user) = found.Value;

        fullName = fullName?.Trim(); roomNumber = roomNumber?.Trim().ToUpperInvariant(); cellphone = cellphone?.Trim(); email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        string? problem = null;
        if (string.IsNullOrEmpty(fullName) || fullName.Length > 120) problem = "Enter the student's full name.";
        else if (string.IsNullOrEmpty(roomNumber) || roomNumber.Length > 20) problem = "Enter the room number.";
        else if (cellphone is null || !System.Text.RegularExpressions.Regex.IsMatch(cellphone, @"^(\+27|0)\d{9}$")) problem = "Enter a valid SA number, e.g. 0712345678.";
        else if (email is not null && !ValidEmail(email)) problem = "Enter a valid email address, or leave it empty.";
        else if (SmsService.ToE164(cellphone) != SmsService.ToE164(student.Cellphone) && await _data.StudentCellphoneTakenAsync(cellphone, studentId)) problem = "Another student already has this cellphone number.";
        if (problem is not null) { TempData["Error"] = problem; return RedirectToAction(nameof(Students)); }

        var phoneChanged = SmsService.ToE164(cellphone!) != SmsService.ToE164(student.Cellphone);
        var emailChanged = !string.Equals(email, student.ContactEmail, StringComparison.OrdinalIgnoreCase);
        var wasRegistered = student.IsRegistered;

        var oldPhone = student.Cellphone;
        user.FullName = fullName!; student.RoomNumber = roomNumber!; student.Cellphone = cellphone!; student.ContactEmail = email;
        // a number is how a student proves who they are, so a changed number always starts set-up again; a changed email does when the box is ticked
        var cancel = wasRegistered && (cancelSignup || phoneChanged);
        // an unfinished First Login (unused codes, set-up tokens) is also cancelled when the number or email it was sent to changes
        var reset = cancel || (!wasRegistered && (phoneChanged || emailChanged));
        if (reset) { await _data.UpdateStudentAsync(student); await _welcome.CancelStudentSignupAsync(student, user, oldPhone); }
        else { await _data.UpdateUserAsync(user); await _data.UpdateStudentAsync(student); }

        var msg = $"{fullName}'s details were saved.";
        if (cancel) msg += " Their earlier sign-up was cancelled and anyone using it was signed out; they must use First Login again.";
        else if (reset) msg += " Any code or link already sent for the old details no longer works.";

        if (reset && (emailChanged || phoneChanged) && user.AccountStatus != AccountStatuses.Active)
            msg += $" No invite was sent because they are {user.AccountStatus}.";
        else if (reset && (emailChanged || phoneChanged))
        {
            if (!InviteAllowed($"student-{studentId}")) msg += " No new invite was sent: 3 already went out in the last hour. Use Send invite later.";
            else
            {
                var sent = new List<string>();
                if (email is not null && emailChanged && await SendStudentInviteEmailAsync(fullName!, email, cellphone!)) sent.Add($"an email to {email}");
                var first = fullName!.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
                if (phoneChanged && _sms.IsConfigured && await _sms.SendAsync(cellphone!, $"ITS ResFix: hi {first}, your account is ready. Open {StudentAppUrl()}, choose First Login and enter this cellphone number to set your password."))
                    sent.Add("an SMS");
                msg += sent.Count > 0 ? $" A new invite was sent: {string.Join(" and ", sent)}." : " No new invite could be sent (check the details, or use Send invite).";
            }
        }
        TempData["Success"] = msg;
        return RedirectToAction(nameof(Students));
    }

    /// <summary>Cancels a student's account set-up (for example when an invite reached the wrong person) and forgets the saved email. Their record and requests stay.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeStudentAccess(int studentId)
    {
        var found = await _data.GetStudentWithUserAsync(studentId);
        if (found is null) return NotFound();
        var (student, user) = found.Value;
        student.ContactEmail = null;
        await _data.UpdateStudentAsync(student);
        await _welcome.CancelStudentSignupAsync(student, user);
        TempData["Success"] = $"{user.DisplayName()}'s access was revoked: they were signed out, any code or link already sent stopped working, and the saved email was removed. Use Send invite when they should start again.";
        return RedirectToAction(nameof(Students));
    }

    // ---------- Categories ----------

    public async Task<IActionResult> Categories(bool add = false)
    {
        ViewBag.OpenAdd = add;
        return View(new CategoriesViewModel
    {
        Categories = await _data.GetCategoriesAsync(),
        Counts = (await _data.GetComplaintsAsync()).GroupBy(r => r.Complaint.CategoryId).ToDictionary(g => g.Key, g => g.Count())
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCategory(CategoriesViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Categories = await _data.GetCategoriesAsync();
            model.Counts = (await _data.GetComplaintsAsync()).GroupBy(r => r.Complaint.CategoryId).ToDictionary(g => g.Key, g => g.Count());
            ViewBag.OpenAdd = true;
            return View("Categories", model);
        }
        await _data.AddCategoryAsync(model.NewName);
        TempData["Success"] = $"Category '{model.NewName.Trim()}' added.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int categoryId)
    {
        try
        {
            await _data.DeleteCategoryAsync(categoryId);
            TempData["Success"] = "Category deleted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Categories));
    }

    // ---------- External company ----------

    public async Task<IActionResult> ExternalCompany()
    {
        ViewBag.EmailConfigured = _email.IsConfigured;
        var company = await _data.GetExternalCompanyAsync();
        var companyVm = company is null ? new ExternalCompanyViewModel() : new ExternalCompanyViewModel
        {
            Exists = true,
            CompanyName = company.CompanyName,
            ApiEndpoint = company.ApiEndpoint,
            ContactEmail = company.ContactEmail,
            ContactPhone = company.ContactPhone,
            IsActive = company.IsActive
        };
        return View(new ExternalCompanyPageViewModel
        {
            Company = companyVm,
            ReassignedComplaints = await _data.GetExternalComplaintsAsync()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExternalCompany(ExternalCompanyViewModel Company)
    {
        if (!ModelState.IsValid)
        {
            return View(new ExternalCompanyPageViewModel
            {
                Company = Company,
                ReassignedComplaints = await _data.GetExternalComplaintsAsync()
            });
        }
        await _data.SaveExternalCompanyAsync(Company);
        TempData["Success"] = "Company details saved.";
        return RedirectToAction(nameof(ExternalCompany));
    }

    // ---------- Auto-assignment rules ----------

    public async Task<IActionResult> AssignmentRules() => View(await BuildRulesViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignmentRules(AssignmentRulesViewModel form)
    {
        if (!ModelState.IsValid)
        {
            var vm = await BuildRulesViewModel();
            vm.CategoryId = form.CategoryId;
            vm.WorkerId = form.WorkerId;
            return View(vm);
        }
        if (!await _data.SaveRuleAsync(form.CategoryId!.Value, form.WorkerId!.Value))
        {
            TempData["Error"] = "That category or worker no longer exists. The rule was not saved.";
            return RedirectToAction(nameof(AssignmentRules));
        }
        TempData["Success"] = "Auto-assignment rule saved.";
        return RedirectToAction(nameof(AssignmentRules));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRule(int ruleId)
    {
        await _data.DeleteRuleAsync(ruleId);
        TempData["Success"] = "Rule removed.";
        return RedirectToAction(nameof(AssignmentRules));
    }

    private async Task<AssignmentRulesViewModel> BuildRulesViewModel() => new()
    {
        Rules = await _data.GetRulesAsync(),
        CompanyName = (await _data.GetExternalCompanyAsync())?.CompanyName,
        Categories = await _data.GetCategoriesAsync(),
        Workers = await _data.GetWorkersAsync()
    };

    // ---------- Reports ----------

    public async Task<IActionResult> Reports(int range = 30)
    {
        range = range is 7 or 30 or 90 or 0 ? range : 30;
        var everything = await _data.GetComplaintsAsync();
        var since = range == 0 ? DateTime.MinValue : DateTime.UtcNow.AddDays(-range);
        var rows = everything.Where(r => r.Complaint.CreatedAt.AsUtc() >= since).ToList();
        var ratings = await _data.GetRatingsAsync();

        bool IsOpen(ComplaintRow r) => r.Complaint.StatusId is StatusIds.Submitted or StatusIds.InProgress;
        var real = rows.Where(r => r.Complaint.StatusId != StatusIds.Rejected).ToList();
        var resolved = real.Where(r => r.Complaint.StatusId == StatusIds.Resolved && r.Complaint.ResolvedAt is not null).ToList();
        double? Hours(IEnumerable<ComplaintRow> rs)
        {
            var l = rs.Where(r => r.Complaint.AssignedAt is not null && r.Complaint.ResolvedAt is not null).ToList();
            return l.Count == 0 ? null : l.Average(r => (r.Complaint.ResolvedAt!.Value.AsUtc() - r.Complaint.AssignedAt!.Value.AsUtc()).TotalHours);
        }
        double? Rating(IEnumerable<ComplaintRow> rs)
        {
            var l = rs.Where(r => ratings.ContainsKey(r.Complaint.ComplaintId)).Select(r => (double)ratings[r.Complaint.ComplaintId]).ToList();
            return l.Count == 0 ? null : l.Average();
        }

        // volume over time: daily for up to a month, weekly beyond that
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, DisplayHelper.SaZone).Date;
        DateTime Local(ComplaintRow r) => TimeZoneInfo.ConvertTimeFromUtc(r.Complaint.CreatedAt.AsUtc(), DisplayHelper.SaZone).Date;
        var weekly = range is 90 or 0;
        var trend = new List<ReportCount>();
        if (!weekly)
        {
            for (var i = (range == 7 ? 6 : 29); i >= 0; i--)
            {
                var d = today.AddDays(-i);
                trend.Add(new ReportCount(d.ToString(range == 7 ? "ddd" : "d MMM"), real.Count(r => Local(r) == d)));
            }
        }
        else
        {
            var weeks = range == 90 ? 13 : 12;
            for (var i = weeks - 1; i >= 0; i--)
            {
                var end = today.AddDays(-7 * i); var start = end.AddDays(-6);
                trend.Add(new ReportCount(start.ToString("d MMM"), real.Count(r => Local(r) >= start && Local(r) <= end)));
            }
        }

        return View(new ReportsViewModel
        {
            Range = range,
            Total = real.Count,
            Open = real.Count(IsOpen),
            Resolved = real.Count(r => r.Complaint.StatusId == StatusIds.Resolved),
            Rejected = rows.Count - real.Count,
            Emergencies = real.Count(r => r.IsEmergency),
            ResolutionRate = real.Count == 0 ? null : (int)Math.Round(100.0 * real.Count(r => r.Complaint.StatusId == StatusIds.Resolved) / real.Count),
            OnTimeRate = DisplayHelper.OnTimeRate(real.Select(r => r.Complaint)),
            AvgHours = Hours(resolved),
            AvgRating = Rating(real),
            RatingCount = real.Count(r => ratings.ContainsKey(r.Complaint.ComplaintId)),
            ByCategory = real.GroupBy(r => r.CategoryName)
                .Select(g => new ReportCount(g.Key, g.Count(), g.Count(r => r.Complaint.StatusId == StatusIds.Resolved)))
                .OrderByDescending(x => x.Count).ToList(),
            Trend = trend,
            TrendUnit = weekly ? "week" : "day",
            // suspended and removed workers stay in the history, but only when they actually had work in this period
            Workers = (await _data.GetWorkersForManagementAsync()).Select(w =>
            {
                var mine = real.Where(r => r.Complaint.AssignedTo == w.WorkerId).ToList();
                var done = mine.Where(r => r.Complaint.StatusId == StatusIds.Resolved).ToList();
                var label = w.AccountStatus == AccountStatuses.Active ? w.Name : $"{w.Name} ({w.AccountStatus})";
                return (Active: w.AccountStatus == AccountStatuses.Active, Row: new WorkerReport(label, mine.Count, done.Count, DisplayHelper.OnTimeRate(mine.Select(r => r.Complaint)), Hours(done), Rating(done)));
            }).Where(x => x.Active || x.Row.Assigned > 0).Select(x => x.Row)
              .OrderByDescending(w => w.Completed).ThenByDescending(w => w.Assigned).ToList(),
            Rooms = real.GroupBy(r => r.Complaint.RoomNumber).Select(g => new ReportCount(g.Key, g.Count()))
                .OrderByDescending(x => x.Count).ThenBy(x => x.Label).Take(5).ToList(),
            AiRejected = rows.Count(r => r.IsRejected),
            AiRecategorised = real.Count(r => r.Complaint.OriginalCategoryId is not null),
            AppealsPending = rows.Count(r => r.Complaint.AppealStatus == AppealStatuses.Pending),
            AppealsApproved = rows.Count(r => r.Complaint.AppealStatus == AppealStatuses.Approved),
            AppealsDeclined = rows.Count(r => r.Complaint.AppealStatus == AppealStatuses.Declined),
            AutoAssigned = real.Count(r => r.Complaint.AssignedType == AssignmentTypes.Auto),
            ManualAssigned = real.Count(r => r.Complaint.AssignedType == AssignmentTypes.Manual),
            Overdue = real.Count(r => IsOpen(r) && r.Timer.IsExpired)
        });
    }
}
