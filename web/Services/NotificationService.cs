using ITSResFix.Staff.Models.Db;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

/// <summary>Creates in-app notifications and, for students who opted in, sends the same news by SMS.</summary>
public class NotificationService
{
    private readonly Supabase.Client _db;
    private readonly SmsService _sms;
    private readonly PushService _push;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(Supabase.Client db, SmsService sms, PushService push, ILogger<NotificationService> log)
    {
        _db = db; _sms = sms; _push = push; _log = log;
    }

    /// <summary>Adds the in-app notification; when <paramref name="sms"/> is true also texts students who enabled SMS alerts.</summary>
    public async Task NotifyAsync(int studentId, int? complaintId, string message, bool sms = false)
    {
        await _db.From<StudentNotification>().Insert(new StudentNotification { StudentId = studentId, ComplaintId = complaintId, Message = message });
        await _push.SendAsync(studentId, message, complaintId);
        if (sms) await SmsOnlyAsync(studentId, message);
    }

    /// <summary>Push (and optionally SMS) without a new in-app row: the database trigger already created that one.</summary>
    public async Task AlertOnlyAsync(int studentId, int? complaintId, string message, bool sms)
    {
        await _push.SendAsync(studentId, message, complaintId);
        if (sms) await SmsOnlyAsync(studentId, message);
    }

    /// <summary>SMS without a new in-app notification (the database trigger already created that one).</summary>
    public async Task SmsOnlyAsync(int studentId, string message)
    {
        if (!_sms.IsConfigured) return;
        try
        {
            var student = (await _db.From<Student>().Filter("student_id", Operator.Equals, studentId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
            if (student is null || !student.SmsAlerts || string.IsNullOrWhiteSpace(student.Cellphone)) return;
            await _sms.SendAsync(student.Cellphone, "ITS ResFix: " + message);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not send SMS alert to student {Id}", studentId);
        }
    }
}
