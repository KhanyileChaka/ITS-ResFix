using System.Collections.Concurrent;
using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.Db;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Tells a worker, by email and SMS, that a complaint has been assigned to them. Never throws: a failed message is logged and the
/// assignment stands (the job is on the worker's dashboard either way).
/// </summary>
public class WorkerNotifier
{
    // the same job is not announced twice to the same worker within a couple of minutes (a review and a reassignment can overlap)
    private static readonly ConcurrentDictionary<string, DateTime> Recent = new();

    private readonly Supabase.Client _db;
    private readonly EmailService _email;
    private readonly SmsService _sms;
    private readonly IConfiguration _config;
    private readonly ILogger<WorkerNotifier> _log;

    public WorkerNotifier(Supabase.Client db, EmailService email, SmsService sms, IConfiguration config, ILogger<WorkerNotifier> log)
    {
        _db = db; _email = email; _sms = sms; _config = config; _log = log;
    }

    public record Result(bool Emailed, bool Texted);

    public async Task<Result> NotifyAssignedAsync(Complaint c)
    {
        try
        {
            if (c.AssignedTo is not int workerId || c.StatusId is StatusIds.Resolved or StatusIds.Rejected) return new(false, false);
            var key = $"{c.ComplaintId}|{workerId}";
            var now = DateTime.UtcNow;
            foreach (var old in Recent.Where(kv => now - kv.Value > TimeSpan.FromMinutes(10)).Select(kv => kv.Key).ToList()) Recent.TryRemove(old, out _);
            if (Recent.TryGetValue(key, out var at) && now - at < TimeSpan.FromMinutes(2)) return new(false, false);
            Recent[key] = now;

            var worker = (await _db.From<InternalWorker>().Filter("worker_id", Operator.Equals, workerId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
            var user = worker is null ? null : (await _db.From<AppUser>().Filter("user_id", Operator.Equals, worker.UserId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
            if (worker is null || user is null || user.AccountStatus != AccountStatuses.Active) return new(false, false);

            var category = (await _db.From<Category>().Filter("category_id", Operator.Equals, c.CategoryId.ToString()).Limit(1).Get()).Models.FirstOrDefault()?.Name ?? "Uncategorised";
            var job = $"C{c.ComplaintId:D3}";
            var baseUrl = _config["Hosting:PublicUrl"]?.TrimEnd('/');
            var link = string.IsNullOrWhiteSpace(baseUrl) ? "" : $"{baseUrl}/Worker/Job/{c.ComplaintId}";
            var deadline = c.Deadline.ToSaTime("ddd d MMM, HH:mm");
            var name = user.DisplayName();

            bool emailed = false, texted = false;
            if (CanReceiveEmail(user.Email) && _email.IsConfigured)
            {
                var (html, text) = EmailTemplates.JobAssigned(name, job, category, c.RoomNumber, c.Priority, c.Description, deadline, link);
                emailed = await _email.SendAsync(user.Email, $"{(c.Priority == Priorities.Emergency ? "EMERGENCY: " : "")}New job {job} assigned to you", text, html);
            }
            var phone = !string.IsNullOrWhiteSpace(worker.Phone) ? worker.Phone : user.Cellphone;
            if (!string.IsNullOrWhiteSpace(phone) && _sms.IsConfigured)
            {
                var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
                var body = $"ITS ResFix: hi {first}, {(c.Priority == Priorities.Emergency ? "EMERGENCY " : "")}job {job} ({category}, room {c.RoomNumber}) was assigned to you. Respond by {deadline}." + (link.Length > 0 ? $" {link}" : "");
                texted = await _sms.SendAsync(phone!, body);
            }
            _log.LogInformation("Worker {WorkerId} told about {Job}: email={Email} sms={Sms}", workerId, job, emailed, texted);
            return new(emailed, texted);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not tell the worker about complaint {Id}", c.ComplaintId);
            return new(false, false);
        }
    }

    private static bool CanReceiveEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@') && !email.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
}
