using System.Security.Claims;
using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.ViewModels;
using ITSResFix.Staff.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ITSResFix.Staff.Controllers;

[Authorize(Roles = Roles.Worker)]
public class WorkerController : Controller
{
    private static readonly int[] WorkerSettableStatuses = { StatusIds.Submitted, StatusIds.InProgress, StatusIds.Resolved };

    private readonly ResFixDataService _data;
    private readonly StaffSessionValidator _sessions;

    private readonly ChatService _chat;

    public WorkerController(ResFixDataService data, StaffSessionValidator sessions, ChatService chat) { _data = data; _sessions = sessions; _chat = chat; }

    /// <summary>worker_id stored in the auth cookie at login.</summary>
    private int CurrentWorkerId => int.Parse(User.FindFirst(ResFixClaims.WorkerId)!.Value);

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ---------- Dashboard & jobs ----------

    public async Task<IActionResult> Dashboard()
    {
        await _data.SyncExpiredTimersAsync();
        var worker = await _data.GetWorkerAsync(CurrentWorkerId);
        return View(new WorkerJobsViewModel
        {
            Jobs = await _data.GetComplaintsForWorkerAsync(CurrentWorkerId),
            TableHeading = "My Assigned Jobs",
            IsAvailable = worker?.IsAvailable ?? true
        });
    }

    public async Task<IActionResult> Jobs(string? q, string filter = "all")
    {
        await _data.SyncExpiredTimersAsync();
        filter = filter is "new" or "inprogress" or "urgent" or "done" ? filter : "all";
        q = q?.Trim();
        if (q?.Length > 60) q = q[..60];

        var all = await _data.GetComplaintsForWorkerAsync(CurrentWorkerId);
        var shown = all.Where(j => filter switch
            {
                "new" => j.Complaint.StatusId == StatusIds.Submitted,
                "inprogress" => j.Complaint.StatusId == StatusIds.InProgress,
                "done" => j.Complaint.StatusId == StatusIds.Resolved,
                "urgent" => j.Complaint.StatusId != StatusIds.Resolved && (j.IsEmergency || j.Timer.IsExpired),
                _ => true
            })
            .Where(j => string.IsNullOrEmpty(q)
                || DisplayHelper.JobCode(j.Complaint.ComplaintId).Contains(q, StringComparison.OrdinalIgnoreCase)
                || j.Complaint.RoomNumber.Contains(q, StringComparison.OrdinalIgnoreCase)
                || j.CategoryName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || j.Complaint.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            // open jobs first, emergencies and nearest deadline at the top, finished ones last
            .OrderBy(j => j.Complaint.StatusId == StatusIds.Resolved)
            .ThenByDescending(j => j.IsEmergency || j.Timer.IsExpired)
            .ThenBy(j => j.Complaint.Deadline ?? DateTime.MaxValue)
            .ToList();

        return View(new WorkerJobsViewModel { Jobs = shown, AllJobs = all, Filter = filter, Query = q, TableHeading = "All Assigned Jobs" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAvailability(bool available, string? returnTo)
    {
        if (!await _data.SetAvailabilityAsync(CurrentWorkerId, available)) return NotFound();
        TempData["Success"] = available ? "You are now shown as available." : "You are now shown as off duty. Your assigned jobs are not affected.";
        return returnTo == "profile" ? RedirectToAction(nameof(Profile)) : RedirectToAction(nameof(Dashboard));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduleVisit(int id, string? visitAt)
    {
        if (!DateTime.TryParseExact(visitAt, "yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var local))
        {
            TempData["Error"] = "Pick a date and time for the visit.";
            return RedirectToAction(nameof(Job), new { id });
        }

        switch (await _data.ScheduleVisitAsync(id, CurrentWorkerId, DisplayHelper.FromSaLocal(local)))
        {
            case ResFixDataService.VisitResult.NotFound: return NotFound();
            case ResFixDataService.VisitResult.Closed: TempData["Error"] = "This job is already closed."; break;
            case ResFixDataService.VisitResult.TooSoon: TempData["Error"] = "Choose a time that has not passed."; break;
            case ResFixDataService.VisitResult.TooFar: TempData["Error"] = "Visits can be planned up to 7 days ahead."; break;
            default: TempData["Success"] = "Visit planned. The student has been told."; break;
        }
        return RedirectToAction(nameof(Job), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Job(int id)
    {
        var row = await _data.GetComplaintRowAsync(id);
        if (row is null || row.Complaint.AssignedTo != CurrentWorkerId) return NotFound();

        ViewBag.Messages = await _data.GetMessagesAsync(id);
        return View(new WorkerComplaintViewModel
        {
            Row = row,
            StatusId = row.Complaint.StatusId,
            Notes = row.Complaint.WorkerNotes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PostMessage(int id, string? body)
    {
        var complaint = await _data.GetComplaintAsync(id);
        if (complaint is null || complaint.AssignedTo != CurrentWorkerId) return NotFound();
        var sent = await _data.PostStaffMessageAsync(id, User.Identity?.Name ?? "Maintenance team", body ?? "");
        if (sent) _chat.StopTyping(id, ChatService.Staff);
        if (Request.Headers["X-Requested-With"] == "fetch")
            return Json(new { ok = sent, error = sent ? null : "The message could not be sent. You can send up to 10 messages per hour on one request." });
        if (sent) TempData["Success"] = "Message sent to the student.";
        else TempData["Error"] = "The message could not be sent. You can send up to 10 messages per hour on one request.";
        return RedirectToAction(nameof(Job), new { id });
    }

    /// <summary>The conversation for the open job page (polled every few seconds). seen=true means the page is on screen.</summary>
    [HttpGet]
    public async Task<IActionResult> JobChat(int id, bool seen = false)
    {
        var complaint = await _data.GetComplaintAsync(id);
        if (complaint is null || complaint.AssignedTo != CurrentWorkerId) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Json(await _chat.ThreadAsync(id, ChatService.Staff, seen));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> JobTyping(int id)
    {
        if (!_chat.AllowPing(id, ChatService.Staff)) return Ok();
        var complaint = await _data.GetComplaintAsync(id);
        if (complaint is null || complaint.AssignedTo != CurrentWorkerId) return NotFound();
        _chat.Typing(id, ChatService.Staff, User.Identity?.Name ?? "Maintenance team");
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, int statusId, string? notes, bool complete = false)
    {
        if (complete) statusId = StatusIds.Resolved;

        if (!WorkerSettableStatuses.Contains(statusId))
        {
            TempData["Error"] = "Choose New, In Progress or Resolved.";
            return RedirectToAction(nameof(Job), new { id });
        }

        if (notes?.Length > 2000) notes = notes[..2000];

        if (!await _data.UpdateWorkProgressAsync(id, CurrentWorkerId, statusId, notes))
            return NotFound();

        TempData["Success"] = statusId == StatusIds.Resolved
            ? $"Job {DisplayHelper.JobCode(id)} marked as complete."
            : $"Job {DisplayHelper.JobCode(id)} updated to {DisplayHelper.WorkerStatusLabel(statusId)}.";
        return RedirectToAction(nameof(Job), new { id });
    }

    // ---------- Work history ----------

    public async Task<IActionResult> History(string status = "all")
    {
        status = status is "completed" or "inprogress" ? status : "all";
        var jobs = await _data.GetComplaintsForWorkerAsync(CurrentWorkerId);

        var filtered = jobs.Where(j => status switch
        {
            "completed" => j.Complaint.StatusId == StatusIds.Resolved,
            "inprogress" => j.Complaint.StatusId == StatusIds.InProgress,
            _ => j.Complaint.StatusId is StatusIds.Resolved or StatusIds.InProgress
        })
        .OrderByDescending(j => j.Complaint.ResolvedAt ?? DateTime.MaxValue)
        .ToList();

        return View(new WorkerHistoryViewModel
        {
            Jobs = filtered,
            Filter = status,
            TotalCompleted = jobs.Count(j => j.Complaint.StatusId == StatusIds.Resolved),
            OnTimeRate = DisplayHelper.OnTimeRate(jobs.Select(j => j.Complaint))
        });
    }

    // ---------- Profile ----------

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        ViewBag.MustChange = User.HasClaim(ResFixClaims.MustChange, "1");
        return View(await BuildProfileAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProfile([Bind(Prefix = "Edit")] EditProfileViewModel form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage;
            return RedirectToAction(nameof(Profile));
        }

        var user = await _data.GetUserByIdAsync(CurrentUserId);
        var worker = await _data.GetWorkerAsync(CurrentWorkerId);
        if (user is null || worker is null) return NotFound();

        user.FullName = form.FullName.Trim();
        worker.Phone = string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone.Trim();
        await _data.UpdateUserAsync(user);
        await _data.UpdateWorkerAsync(worker);

        // Refresh the cookie so the header shows the new name.
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffPrincipal.Create(user, worker.WorkerId));

        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordViewModel form)
    {
        var user = await _data.GetUserByIdAsync(CurrentUserId);
        if (user is null) return NotFound();

        if (ModelState.IsValid && !PasswordHasher.Verify(form.CurrentPassword, user.PasswordHash))
            ModelState.AddModelError("Password.CurrentPassword", "Your current password is incorrect.");

        if (!ModelState.IsValid)
        {
            ViewBag.MustChange = User.HasClaim(ResFixClaims.MustChange, "1");
            var vm = await BuildProfileAsync();
            return View(nameof(Profile), vm);
        }

        user.PasswordHash = PasswordHasher.Hash(form.NewPassword);
        user.MustChangePassword = false;
        await _data.UpdateUserAsync(user);

        _sessions.Invalidate(user.UserId);
        // Other devices are signed out (their cookie carries the old password fingerprint); this one stays signed in.
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffPrincipal.Create(user, CurrentWorkerId));

        TempData["Success"] = "Your password has been changed.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<WorkerProfileViewModel> BuildProfileAsync()
    {
        var user = await _data.GetUserByIdAsync(CurrentUserId);
        var worker = await _data.GetWorkerAsync(CurrentWorkerId);
        var jobs = await _data.GetComplaintsForWorkerAsync(CurrentWorkerId);

        return new WorkerProfileViewModel
        {
            FullName = user.DisplayName(),
            Email = user?.Email ?? "-",
            Phone = worker?.Phone,
            Specialisation = worker?.Specialisation,
            AssignedAccommodation = worker?.AssignedAccommodation,
            AutoAssignmentEnabled = await _data.WorkerHasAutoAssignmentAsync(CurrentWorkerId),
            ResponseRate = DisplayHelper.OnTimeRate(jobs.Select(j => j.Complaint)),
            Edit = new EditProfileViewModel { FullName = user?.FullName ?? string.Empty, Phone = worker?.Phone }
        };
    }
}
