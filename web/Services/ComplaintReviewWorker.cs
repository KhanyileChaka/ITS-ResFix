using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.Db;
using ITSResFix.Staff.Helpers;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Polls for complaints the AI has not reviewed yet, rejects junk and fixes wrong categories.
/// Failures (network, bad AI output) leave the complaint unreviewed so it is retried next cycle.
/// </summary>
public class ComplaintReviewWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

    private readonly Supabase.Client _db;
    private readonly AiTriageService _ai;
    private readonly ILogger<ComplaintReviewWorker> _log;
    private readonly CloudinaryService _images;
    private readonly NotificationService _notify;

    private readonly WorkerNotifier _workers;

    public ComplaintReviewWorker(Supabase.Client db, AiTriageService ai, CloudinaryService images, NotificationService notify, WorkerNotifier workers, ILogger<ComplaintReviewWorker> log)
    {
        _db = db; _ai = ai; _images = images; _notify = notify; _workers = workers; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_ai.IsConfigured)
        {
            _log.LogWarning("OpenRouter:ApiKey not set - AI complaint review is disabled.");
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try { await ReviewPendingAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { _log.LogError(ex, "Complaint review cycle failed"); }
            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReviewPendingAsync(CancellationToken ct)
    {
        var pending = (await _db.From<Complaint>()
            .Filter("ai_checked", Operator.Equals, "false")
            .Filter("status_id", Operator.NotEqual, StatusIds.Resolved.ToString())
            .Filter("status_id", Operator.NotEqual, StatusIds.Rejected.ToString())
            .Order("created_at", Ordering.Ascending)
            .Limit(10)
            .Get()).Models;
        if (pending.Count == 0) return;

        var categories = (await _db.From<Category>().Get()).Models;

        foreach (var c in pending)
        {
            try { await ReviewOneAsync(c, categories, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "AI review failed for complaint {Id}; will retry", c.ComplaintId);
            }
        }
    }

    private async Task ReviewOneAsync(Complaint c, List<Category> categories, CancellationToken ct)
    {
        TriageVerdict verdict;
        try
        {
            verdict = await _ai.ReviewAsync(c.Description, c.CategoryId, c.Priority, categories, c.PhotoUrl, ct);
        }
        catch (Exception ex) when (c.PhotoUrl is not null && !ct.IsCancellationRequested)
        {
            // Vision call failed (unreadable image, model hiccup). Judge the text alone; the photo stays unreviewed.
            _log.LogWarning(ex, "Photo review failed for complaint {Id}; reviewing text only", c.ComplaintId);
            verdict = await _ai.ReviewAsync(c.Description, c.CategoryId, c.Priority, categories, null, ct);
        }
        // Another copy of the site (a second server, or an overlapping deploy) may have finished this review while the AI was
        // thinking. Saving ours on top would bring back a photo that copy already deleted, so step aside.
        var current = (await _db.From<Complaint>().Select("complaint_id,ai_checked")
            .Filter("complaint_id", Operator.Equals, c.ComplaintId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        if (current is null || current.AiChecked) return;

        var code = $"C{c.ComplaintId:D3}";
        string studentMsg;

        if (!verdict.Valid)
        {
            c.StatusId = StatusIds.Rejected;
            c.AssignedTo = null;
            c.AssignedType = null;
            c.AssignedAt = null;
            c.Deadline = null;
            c.TimerStatus = TimerStatuses.Pending;
            c.AiNote = verdict.Reason;
            studentMsg = $"Your complaint {code} was rejected: {verdict.Reason}";
        }
        else
        {
            studentMsg = $"Your complaint {code} was reviewed and accepted.";
            var priorityNote = "";
            if (verdict.Priority != c.Priority)
            {
                c.Priority = verdict.Priority;
                priorityNote = verdict.Priority == Priorities.Emergency
                    ? " Marked as an emergency." : " Emergency flag removed (not an emergency).";
            }
            if (verdict.CategoryId != c.CategoryId)
            {
                var newName = categories.First(x => x.CategoryId == verdict.CategoryId).Name;
                c.OriginalCategoryId ??= c.CategoryId;
                c.CategoryId = verdict.CategoryId;
                c.AiNote = $"Category corrected by AI to {newName}. {verdict.Reason}".Trim();
                studentMsg = $"Your complaint {code} was moved to the {newName} category.";
                await ReassignForNewCategoryAsync(c);
            }
            else
            {
                c.AiNote = verdict.Reason;
            }
            c.AiNote = (c.AiNote + priorityNote).Trim();
            if (priorityNote.Length > 0) studentMsg += priorityNote;
        }

        if (c.PhotoUrl is not null && verdict.PhotoOk == false)
        {
            var why = string.IsNullOrWhiteSpace(verdict.PhotoReason) ? "It does not show the reported problem." : verdict.PhotoReason!.Trim();
            await _images.DeleteByUrlAsync(c.PhotoUrl, ct);
            c.PhotoUrl = null;
            c.PhotoNote = why;
            studentMsg += $" Your photo was not attached: {why}";
        }

        c.AiChecked = true;
        await _db.From<Complaint>().Update(c);
        await _notify.NotifyAsync(c.StudentId, c.ComplaintId, studentMsg, sms: !verdict.Valid);
        // an accepted complaint that has a worker: tell that worker (email + SMS)
        if (verdict.Valid && c.AssignedTo is not null) await _workers.NotifyAssignedAsync(c);
        _log.LogInformation("Reviewed {Code}: valid={Valid} category={Cat}", code, verdict.Valid, c.CategoryId);
    }

    /// <summary>A suspended or removed worker is never given new work.</summary>
    private async Task<bool> WorkerIsActiveAsync(int workerId)
    {
        var worker = (await _db.From<InternalWorker>().Filter("worker_id", Operator.Equals, workerId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        if (worker is null) return false;
        var user = (await _db.From<AppUser>().Filter("user_id", Operator.Equals, worker.UserId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        return user is { AccountStatus: AccountStatuses.Active };
    }

    /// <summary>Only touches complaints the system auto-assigned; manual assignments are left alone.</summary>
    private async Task ReassignForNewCategoryAsync(Complaint c)
    {
        if (c.AssignedType != AssignmentTypes.Auto && c.AssignedTo is not null) return;

        var rule = (await _db.From<AutoAssignmentRule>()
            .Filter("category_id", Operator.Equals, c.CategoryId.ToString())
            .Limit(1).Get()).Models.FirstOrDefault();

        if (rule is null || !await WorkerIsActiveAsync(rule.WorkerId))
        {
            c.AssignedTo = null; c.AssignedType = null; c.AssignedAt = null; c.Deadline = null;
            c.StatusId = StatusIds.Submitted; c.TimerStatus = TimerStatuses.Pending;
            return;
        }

        var now = DateTime.UtcNow;
        c.AssignedTo = rule.WorkerId;
        c.AssignedType = AssignmentTypes.Auto;
        c.AssignedAt = now;
        c.Deadline = now.AddHours(TimerHelper.DeadlineHours);
        c.TimerStatus = TimerStatuses.Active;
    }
}
