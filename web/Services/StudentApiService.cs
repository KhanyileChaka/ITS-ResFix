using System.Security.Cryptography;
using System.Text;
using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.Db;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

public class StudentApiException : Exception
{
    public int Status { get; }
    public StudentApiException(int status, string message) : base(message) => Status = status;
}

public record StudentDto(int UserId, int StudentId, string Email, string FullName, string StudentNumber, string RoomNumber, string Cellphone, bool IsRegistered, bool SmsAlerts, bool SmsAvailable, bool PushAvailable);
public record LoginResultDto(string Token, StudentDto Student);
public record ComplaintDto(int ComplaintId, int CategoryId, string CategoryName, string RoomNumber, string Description, string? PhotoUrl,
    int StatusId, string StatusName, string Priority, string? WorkerName, DateTime? Deadline, DateTime? AssignedAt, DateTime? ResolvedAt,
    DateTime CreatedAt, string TimerStatus, string? WorkerNotes, string? AiNote, int? Rating, string? RatingComment, string? PhotoNote, string? OriginalCategoryName,
    string? AppealStatus, string? AppealReason, string? AppealResponse);
public record MessageDto(long Id, string Sender, string? SenderName, string Body, string? PhotoUrl, DateTime CreatedAt, bool Seen = false);
public record ChatDto(List<MessageDto> Messages, string? TypingName);
public record CategoryDto(int CategoryId, string Name);
public record NotificationDto(long Id, string Message, bool IsRead, DateTime CreatedAt, int? ComplaintId);
public record ReviewDto(bool Ready, int StatusId, int CategoryId, string CategoryName, string? OriginalCategoryName, string Priority, string? AiNote, string? PhotoNote);

/// <summary>
/// All student-facing data access. Runs with the server-side service_role client; every method receives the
/// student id from the verified token and scopes its queries to it, so students can only touch their own data.
/// </summary>
public class StudentApiService
{
    private const int MaxPhotoBytes = 5 * 1024 * 1024;
    private static readonly string DummyHash = PasswordHasher.Hash("not-a-real-password");

    private readonly Supabase.Client _db;
    private readonly StudentTokenService _tokens;
    private readonly LoginThrottle _throttle;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<StudentApiService> _log;
    private readonly CloudinaryService _images;
    private readonly SmsService _sms;
    private readonly NotificationService _notify;
    private readonly AiTriageService _ai;
    private readonly PushService _push;

    public StudentApiService(Supabase.Client db, StudentTokenService tokens, LoginThrottle throttle,
        IConfiguration config, IHostEnvironment env, ILogger<StudentApiService> log, CloudinaryService images,
        SmsService sms, NotificationService notify, AiTriageService ai, PushService push, StudentSessionValidator sessions, ChatService chat, EmailService email)
    {
        _db = db; _tokens = tokens; _throttle = throttle; _config = config; _env = env; _log = log; _images = images;
        _sms = sms; _notify = notify; _ai = ai; _push = push; _sessions = sessions; _chat = chat; _email = email;
    }

    private readonly StudentSessionValidator _sessions;
    private readonly ChatService _chat;
    private readonly EmailService _email;

    // ---------- helpers ----------

    public static string NormalizePhone(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.StartsWith("27") && digits.Length == 11) digits = "0" + digits[2..];
        return digits;
    }

    private async Task<Student?> FindStudentAsync(string normalizedPhone)
    {
        if (normalizedPhone.Length < 9) return null;
        var variants = new[] { normalizedPhone, "+27" + normalizedPhone[1..], "27" + normalizedPhone[1..] };
        foreach (var v in variants)
        {
            var s = (await _db.From<Student>().Filter("cellphone", Operator.Equals, v).Limit(1).Get()).Models.FirstOrDefault();
            if (s is not null) return s;
        }
        return null;
    }

    private async Task<Student> GetStudentAsync(int studentId) =>
        (await _db.From<Student>().Filter("student_id", Operator.Equals, studentId.ToString()).Limit(1).Get()).Models.FirstOrDefault()
        ?? throw new StudentApiException(401, "Account not found.");

    private async Task<AppUser> GetUserAsync(int userId) =>
        (await _db.From<AppUser>().Filter("user_id", Operator.Equals, userId.ToString()).Limit(1).Get()).Models.FirstOrDefault()
        ?? throw new StudentApiException(401, "Account not found.");

    private StudentDto ToDto(Student s, AppUser u) =>
        new(u.UserId, s.StudentId, u.Email, u.FullName, s.StudentNumber, s.RoomNumber, s.Cellphone, s.IsRegistered, s.SmsAlerts, _sms.IsConfigured, _push.IsConfigured);

    /// <summary>A suspended or removed student cannot sign in or set up an account.</summary>
    private static void RequireActive(AppUser user)
    {
        if (user.AccountStatus != AccountStatuses.Active)
            throw new StudentApiException(403, user.AccountStatus == AccountStatuses.Suspended
                ? "Your account is suspended. Please contact your accommodation manager."
                : "This account is no longer active. Please contact your accommodation manager.");
    }

    private static void ValidatePassword(string? pw)
    {
        if (string.IsNullOrEmpty(pw) || pw.Length < 8 || pw.Length > 100 || !pw.Any(char.IsDigit) || !pw.Any(char.IsUpper))
            throw new StudentApiException(400, "Password must be at least 8 characters with a number and a capital letter.");
    }

    private string Hmac(string phone, string code)
    {
        using var h = new HMACSHA256(_tokens.HmacKey);
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes($"{phone}|{code}")));
    }

    // ---------- auth ----------

    public async Task<bool> IsRegisteredAsync(string cellphone)
    {
        // An unknown number looks exactly like a student who has not set up yet, so this cannot be used to find out who lives here.
        var s = await FindStudentAsync(NormalizePhone(cellphone));
        return s is { IsRegistered: true };
    }

    public async Task<LoginResultDto> LoginAsync(string cellphone, string password, string ip)
    {
        var phone = NormalizePhone(cellphone);
        // Tier 1: one device/IP guessing one account locks after 5 misses. Tier 2: any number of IPs together
        // may only miss 20 times per account, which caps distributed guessing without punishing other students.
        var key = $"stu|{ip}|{phone}";
        var acctKey = $"stu-acct|{phone}";
        if (_throttle.IsLocked(key, out var left) || _throttle.IsLocked(acctKey, out left))
            throw new StudentApiException(429, $"Too many attempts. Try again in {LoginThrottle.Minutes(left)} minute(s).");

        var student = await FindStudentAsync(phone);
        var user = student is null ? null : (await _db.From<AppUser>().Filter("user_id", Operator.Equals, student.UserId.ToString()).Limit(1).Get()).Models.FirstOrDefault();

        // Always run a bcrypt verify so response time does not reveal whether the number exists.
        var ok = PasswordHasher.Verify(password ?? "", user?.PasswordHash ?? DummyHash) && user is not null && student is not null && user.Role == Roles.Student;
        if (!ok)
        {
            _throttle.Fail(key);
            _throttle.Fail(acctKey, 20);
            throw new StudentApiException(401, "Incorrect cellphone number or password.");
        }
        RequireActive(user!);
        if (!student!.IsRegistered)
            throw new StudentApiException(403, "Please use First Login to set up your password.");

        _throttle.Reset(key);
        _throttle.Reset(acctKey);
        return new LoginResultDto(_tokens.IssueSession(user!.UserId, student.StudentId), ToDto(student, user));
    }

    public async Task RequestOtpAsync(string cellphone, string ip)
    {
        var phone = NormalizePhone(cellphone);
        var key = $"otpreq|{phone}";   // per account: SMS costs money and a code is only ever for one number
        if (_throttle.IsLocked(key, out var left))
            throw new StudentApiException(429, $"Too many requests. Try again in {LoginThrottle.Minutes(left)} minute(s).");
        _throttle.Fail(key); // each request counts; 5 requests / 15 min
        // A generous cap per network address as well, so one machine cannot sweep through phone numbers to see which exist.
        // It is high on purpose: many students share the residence network and must not be locked out by each other.
        var ipKey = $"otpip|{ip}";
        if (_throttle.IsLocked(ipKey, out left))
            throw new StudentApiException(429, $"Too many requests from this network. Try again in {LoginThrottle.Minutes(left)} minute(s).");
        _throttle.Fail(ipKey, 60);

        var student = await FindStudentAsync(phone);
        if (student is { IsRegistered: true }) throw new StudentApiException(409, "This number is already registered. Please log in.");
        if (student is null || (await GetUserAsync(student.UserId)).AccountStatus != AccountStatuses.Active)
        {
            // Same answer, and about the same wait as a real text, so the reply does not show whether the number exists or is suspended.
            await Task.Delay(Random.Shared.Next(500, 1100));
            return;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var old = (await _db.From<StudentOtp>().Filter("cellphone", Operator.Equals, student.Cellphone)
            .Filter("used", Operator.Equals, "false").Get()).Models;
        foreach (var o in old) { o.Used = true; await _db.From<StudentOtp>().Update(o); }

        await _db.From<StudentOtp>().Insert(new StudentOtp
        {
            Cellphone = student.Cellphone,
            OtpCode = Hmac(student.Cellphone, code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });

        var text = $"Your ITS ResFix code is {code}. It expires in 5 minutes. Never share it with anyone.";
        if (_sms.IsConfigured)
        {
            if (!await _sms.SendAsync(student.Cellphone, text))
                throw new StudentApiException(502, "We could not send the SMS. Please try again in a moment.");
        }
        else if (_env.IsDevelopment()) _log.LogWarning("DEV OTP for {Phone}: {Code}", student.Cellphone, code);
        else throw new StudentApiException(503, "SMS delivery is not configured. Contact your accommodation manager.");
    }

    // ---------- forgotten password ----------

    /// <summary>
    /// Sends a 6-digit code (SMS, and email too when the student has one saved) to a registered, active student who forgot their password.
    /// Every number gets the same quiet answer and about the same wait, so this cannot be used to find out who lives here.
    /// </summary>
    public async Task RequestResetAsync(string cellphone, string ip)
    {
        var phone = NormalizePhone(cellphone);
        var key = $"rstreq|{phone}";
        if (_throttle.IsLocked(key, out var left))
            throw new StudentApiException(429, $"Too many requests. Try again in {LoginThrottle.Minutes(left)} minute(s).");
        _throttle.Fail(key); // 5 requests / 15 min per number
        var ipKey = $"rstip|{ip}";
        if (_throttle.IsLocked(ipKey, out left))
            throw new StudentApiException(429, $"Too many requests from this network. Try again in {LoginThrottle.Minutes(left)} minute(s).");
        _throttle.Fail(ipKey, 60);

        var student = await FindStudentAsync(phone);
        var user = student is null ? null : await GetUserAsync(student.UserId);
        if (student is null || !student.IsRegistered || user is null || user.AccountStatus != AccountStatuses.Active)
        {
            await Task.Delay(Random.Shared.Next(500, 1100));
            return;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var old = (await _db.From<StudentOtp>().Filter("cellphone", Operator.Equals, student.Cellphone)
            .Filter("used", Operator.Equals, "false").Get()).Models;
        foreach (var o in old) { o.Used = true; await _db.From<StudentOtp>().Update(o); }
        await _db.From<StudentOtp>().Insert(new StudentOtp { Cellphone = student.Cellphone, OtpCode = Hmac(student.Cellphone, code), ExpiresAt = DateTime.UtcNow.AddMinutes(5) });

        var sent = false;
        if (_sms.IsConfigured)
            sent |= await _sms.SendAsync(student.Cellphone, $"Your ITS ResFix password reset code is {code}. It expires in 5 minutes. Never share it with anyone.");
        if (!string.IsNullOrWhiteSpace(student.ContactEmail) && _email.IsConfigured)
        {
            var (html, text) = EmailTemplates.StudentResetCode(user.FullName, code);
            sent |= await _email.SendAsync(student.ContactEmail!, "Your ITS ResFix password reset code", text, html);
        }
        if (!sent && _env.IsDevelopment()) _log.LogWarning("DEV reset code for {Phone}: {Code}", student.Cellphone, code);
        else if (!sent) _log.LogWarning("Could not send a password reset code for student {StudentId}", student.StudentId);
    }

    /// <summary>Checks the code and returns a 10-minute token that can only be used to choose a new password.</summary>
    public async Task<string> VerifyResetAsync(string cellphone, string code, string ip)
    {
        var phone = NormalizePhone(cellphone);
        var key = $"rstver|{phone}";
        if (_throttle.IsLocked(key, out var left))
            throw new StudentApiException(429, $"Too many attempts. Try again in {LoginThrottle.Minutes(left)} minute(s).");

        var student = await FindStudentAsync(phone);
        var user = student is null ? null : await GetUserAsync(student.UserId);
        code = (code ?? "").Trim();
        var valid = false;
        if (student is { IsRegistered: true } && user is { AccountStatus: AccountStatuses.Active })
        {
            var devCode = _config["Student:DevOtpCode"];
            valid = _env.IsDevelopment() && !string.IsNullOrEmpty(devCode) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(devCode));
            if (!valid && code.Length == 6)
            {
                var row = (await _db.From<StudentOtp>().Filter("cellphone", Operator.Equals, student.Cellphone)
                    .Filter("used", Operator.Equals, "false").Order("created_at", Ordering.Descending).Limit(1).Get()).Models.FirstOrDefault();
                if (row is not null && row.ExpiresAt.AsUtc() > DateTime.UtcNow &&
                    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(row.OtpCode), Encoding.UTF8.GetBytes(Hmac(student.Cellphone, code))))
                {
                    row.Used = true;
                    await _db.From<StudentOtp>().Update(row);
                    valid = true;
                }
            }
        }
        if (!valid)
        {
            _throttle.Fail(key);
            throw new StudentApiException(400, "Invalid or expired code. Please try again.");
        }
        _throttle.Reset(key);
        return _tokens.IssuePasswordReset(student!.UserId, student.StudentId);
    }

    /// <summary>Sets the new password. Every session the student already had ends, and the reset token cannot be used again; they sign in with the new password.</summary>
    public async Task ResetPasswordAsync(int studentId, string newPassword, string? tokenId)
    {
        ValidatePassword(newPassword);
        var student = await GetStudentAsync(studentId);
        var user = await GetUserAsync(student.UserId);
        if (!student.IsRegistered) throw new StudentApiException(409, "Please use First Login to set up your account.");
        RequireActive(user);

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await _db.From<AppUser>().Update(user);

        var open = (await _db.From<StudentOtp>().Filter("cellphone", Operator.Equals, student.Cellphone).Filter("used", Operator.Equals, "false").Get()).Models;
        foreach (var o in open) { o.Used = true; await _db.From<StudentOtp>().Update(o); }
        // a dated marker: tokens issued before now (old sessions, this reset token) are refused from here on
        await _db.From<PasswordResetRow>().Insert(new PasswordResetRow
        {
            UserId = user.UserId,
            TokenHash = StudentSessionValidator.RevokedPrefix + Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow, UsedAt = DateTime.UtcNow
        });
        _sessions.Invalidate(user.UserId);
        _sessions.MarkUsed(tokenId);
    }

    public async Task<string> VerifyOtpAsync(string cellphone, string code, string ip)
    {
        var phone = NormalizePhone(cellphone);
        var key = $"otpver|{phone}";   // per account: a 6-digit code must not be brute-forceable from many addresses
        if (_throttle.IsLocked(key, out var left))
            throw new StudentApiException(429, $"Too many attempts. Try again in {LoginThrottle.Minutes(left)} minute(s).");

        var student = await FindStudentAsync(phone);
        if (student is { IsRegistered: true }) throw new StudentApiException(409, "This number is already registered. Please log in.");
        if (student is null || (await GetUserAsync(student.UserId)).AccountStatus != AccountStatuses.Active)
        {
            _throttle.Fail(key);
            throw new StudentApiException(400, "Invalid or expired code. Please try again.");
        }

        code = (code ?? "").Trim();
        var devCode = _config["Student:DevOtpCode"];
        var devOk = _env.IsDevelopment() && !string.IsNullOrEmpty(devCode) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(devCode));

        var valid = devOk;
        if (!valid && code.Length == 6)
        {
            var row = (await _db.From<StudentOtp>().Filter("cellphone", Operator.Equals, student.Cellphone)
                .Filter("used", Operator.Equals, "false").Order("created_at", Ordering.Descending).Limit(1).Get()).Models.FirstOrDefault();
            if (row is not null && row.ExpiresAt.AsUtc() > DateTime.UtcNow &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(row.OtpCode), Encoding.UTF8.GetBytes(Hmac(student.Cellphone, code))))
            {
                row.Used = true;
                await _db.From<StudentOtp>().Update(row);
                valid = true;
            }
        }

        if (!valid)
        {
            _throttle.Fail(key);
            throw new StudentApiException(400, "Invalid or expired code. Please try again.");
        }
        _throttle.Reset(key);
        return _tokens.IssuePasswordSetup(student.UserId, student.StudentId, student.Cellphone);
    }

    public async Task<LoginResultDto> SetPasswordAsync(int studentId, string newPassword)
    {
        ValidatePassword(newPassword);
        var student = await GetStudentAsync(studentId);
        if (student.IsRegistered) throw new StudentApiException(409, "This account is already set up. Please log in.");
        var user = await GetUserAsync(student.UserId);

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await _db.From<AppUser>().Update(user);
        student.IsRegistered = true;
        await _db.From<Student>().Update(student);
        _sessions.Invalidate(user.UserId); // the cached "not registered yet" answer must not block the session we are about to issue

        return new LoginResultDto(_tokens.IssueSession(user.UserId, student.StudentId), ToDto(student, user));
    }

    public async Task ChangePasswordAsync(int studentId, string current, string newPassword, string ip)
    {
        var key = $"chg|{studentId}";
        if (_throttle.IsLocked(key, out var left))
            throw new StudentApiException(429, $"Too many attempts. Try again in {LoginThrottle.Minutes(left)} minute(s).");
        ValidatePassword(newPassword);

        var student = await GetStudentAsync(studentId);
        var user = await GetUserAsync(student.UserId);
        if (!PasswordHasher.Verify(current ?? "", user.PasswordHash))
        {
            _throttle.Fail(key);
            throw new StudentApiException(400, "Current password is incorrect.");
        }
        _throttle.Reset(key);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await _db.From<AppUser>().Update(user);
    }

    public async Task<StudentDto> GetMeAsync(int studentId)
    {
        var s = await GetStudentAsync(studentId);
        return ToDto(s, await GetUserAsync(s.UserId));
    }

    // ---------- complaints ----------

    public async Task<List<CategoryDto>> GetCategoriesAsync() =>
        (await _db.From<Category>().Order("name", Ordering.Ascending).Get()).Models.Select(c => new CategoryDto(c.CategoryId, c.Name)).ToList();

    public async Task<List<ComplaintDto>> GetComplaintsAsync(int studentId)
    {
        var complaints = (await _db.From<Complaint>()
            .Filter("student_id", Operator.Equals, studentId.ToString())
            .Order("created_at", Ordering.Descending).Get()).Models;
        if (complaints.Count == 0) return new();

        var cats = (await _db.From<Category>().Get()).Models.ToDictionary(c => c.CategoryId, c => c.Name);
        var statuses = (await _db.From<ComplaintStatus>().Get()).Models.ToDictionary(s => s.StatusId, s => s.Name);

        var workerIds = complaints.Where(c => c.AssignedTo.HasValue).Select(c => (object)c.AssignedTo!.Value).Distinct().ToList();
        var workerNames = new Dictionary<int, string>();
        if (workerIds.Count > 0)
        {
            var workers = (await _db.From<InternalWorker>().Filter("worker_id", Operator.In, workerIds).Get()).Models;
            var userIds = workers.Select(w => (object)w.UserId).Distinct().ToList();
            var users = userIds.Count == 0 ? new List<AppUser>()
                : (await _db.From<AppUser>().Select("user_id,email,role,full_name").Filter("user_id", Operator.In, userIds).Get()).Models;
            var byUser = users.ToDictionary(u => u.UserId, u => u.FullName);
            foreach (var w in workers) workerNames[w.WorkerId] = byUser.GetValueOrDefault(w.UserId, "Maintenance");
        }

        var ids = complaints.Select(c => (object)c.ComplaintId).ToList();
        var ratings = (await _db.From<ComplaintRating>().Filter("complaint_id", Operator.In, ids).Get()).Models
            .ToDictionary(r => r.ComplaintId);

        return complaints.Select(c =>
        {
            ratings.TryGetValue(c.ComplaintId, out var r);
            return new ComplaintDto(c.ComplaintId, c.CategoryId, cats.GetValueOrDefault(c.CategoryId, "Unknown"), c.RoomNumber, c.Description,
                c.PhotoUrl, c.StatusId, statuses.GetValueOrDefault(c.StatusId, "Unknown"), c.Priority,
                c.AssignedTo is int w ? workerNames.GetValueOrDefault(w) : null,
                c.Deadline?.AsUtc(), c.AssignedAt?.AsUtc(), c.ResolvedAt?.AsUtc(), c.CreatedAt.AsUtc(), c.TimerStatus,
                c.WorkerNotes, c.AiNote, r?.Rating, r?.Comment, c.PhotoNote,
                c.OriginalCategoryId is int oc ? cats.GetValueOrDefault(oc) : null,
                c.AppealStatus, c.AppealReason, c.AppealResponse);
        }).ToList();
    }

    private async Task<Complaint> GetOwnComplaintAsync(int studentId, int complaintId)
    {
        var c = (await _db.From<Complaint>().Filter("complaint_id", Operator.Equals, complaintId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        // Same response for "missing" and "someone else's" so ids cannot be probed.
        if (c is null || c.StudentId != studentId) throw new StudentApiException(404, "Complaint not found.");
        return c;
    }

    public async Task<int> SubmitComplaintAsync(int studentId, int categoryId, string? description, bool isEmergency, byte[]? photo)
    {
        description = (description ?? "").Trim();
        if (description.Length < 10) throw new StudentApiException(400, "Please describe the problem in at least 10 characters.");
        if (description.Length > 1000) throw new StudentApiException(400, "Description is too long (max 1000 characters).");

        var cats = (await _db.From<Category>().Get()).Models;
        if (cats.All(c => c.CategoryId != categoryId)) throw new StudentApiException(400, "Unknown category.");

        var student = await GetStudentAsync(studentId);

        // Per-account limits (not per IP: students share the residence network). These sit on top of the hourly cap.
        var maxPerMinute = _config.GetValue("Student:MaxComplaintsPerMinute", 3);
        var maxPerDay = _config.GetValue("Student:MaxComplaintsPerDay", 20);
        var maxPhotosPerDay = _config.GetValue("Student:MaxPhotosPerDay", 5);
        var now = DateTime.UtcNow;
        var recent = (await _db.From<Complaint>()
            .Filter("student_id", Operator.Equals, studentId.ToString())
            .Filter("created_at", Operator.GreaterThanOrEqual, now.AddHours(-24).ToString("o"))
            .Get()).Models;
        if (recent.Count(c => c.CreatedAt.AsUtc() >= now.AddMinutes(-1)) >= maxPerMinute)
            throw new StudentApiException(429, "You are sending complaints too quickly. Please wait a minute and try again.");
        if (recent.Count >= maxPerDay)
            throw new StudentApiException(429, $"You have reached today's limit of {maxPerDay} complaints. Please try again tomorrow, or contact your residence manager for urgent issues.");
        // A photo the AI later removed (PhotoNote set) still counts, so rejected photos cannot be re-uploaded endlessly.
        if (photo is { Length: > 0 } && recent.Count(c => c.PhotoUrl != null || c.PhotoNote != null) >= maxPhotosPerDay)
            throw new StudentApiException(429, $"You have reached today's limit of {maxPhotosPerDay} photos. You can still send the complaint without a photo.");

        string? photoUrl = null;
        string? photoPublicId = null;
        if (photo is { Length: > 0 })
        {
            if (photo.Length > MaxPhotoBytes) throw new StudentApiException(400, "Photo is too large (max 5 MB).");
            var ext = SniffImage(photo) ?? throw new StudentApiException(400, "Only JPG, PNG or WebP photos are allowed.");
            if (!_images.IsConfigured) throw new StudentApiException(503, "Photo upload is not available right now. Please send the complaint without a photo.");
            try
            {
                var up = await _images.UploadAsync(photo, ext, studentId);
                photoUrl = up.Url; photoPublicId = up.PublicId;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Photo upload failed for student {StudentId}", studentId);
                throw new StudentApiException(502, "Could not upload the photo. Please try again or send without a photo.");
            }
        }

        try
        {
            var created = (await _db.From<Complaint>().Insert(new Complaint
            {
                StudentId = studentId,
                CategoryId = categoryId,
                RoomNumber = student.RoomNumber,   // taken from the database, never from the client
                Description = description,
                StatusId = StatusIds.Submitted,
                Priority = isEmergency ? Priorities.Emergency : Priorities.Normal,
                TimerStatus = TimerStatuses.Pending,
                PhotoUrl = photoUrl
            })).Model ?? throw new StudentApiException(500, "Could not save the complaint.");
            return created.ComplaintId;
        }
        catch
        {
            if (photoPublicId is not null) await _images.DeleteAsync(photoPublicId);
            throw;
        }
    }

    private static string? SniffImage(byte[] b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        return null;
    }

    public async Task<ReviewDto> GetReviewAsync(int studentId, int complaintId)
    {
        var c = await GetOwnComplaintAsync(studentId, complaintId);
        var cats = (await _db.From<Category>().Get()).Models.ToDictionary(x => x.CategoryId, x => x.Name);
        return new ReviewDto(c.AiChecked, c.StatusId, c.CategoryId, cats.GetValueOrDefault(c.CategoryId, "Other"),
            c.OriginalCategoryId is int o ? cats.GetValueOrDefault(o) : null, c.Priority, c.AiNote, c.PhotoNote);
    }

    public async Task SubmitRatingAsync(int studentId, int complaintId, int rating, bool issueResolved, string? comment)
    {
        if (rating is < 1 or > 5) throw new StudentApiException(400, "Rating must be 1 to 5.");
        var c = await GetOwnComplaintAsync(studentId, complaintId);
        if (c.StatusId != StatusIds.Resolved) throw new StudentApiException(400, "You can only rate a resolved complaint.");
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()[..Math.Min(comment.Trim().Length, 500)];

        var existing = (await _db.From<ComplaintRating>().Filter("complaint_id", Operator.Equals, complaintId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        if (existing is null)
            await _db.From<ComplaintRating>().Insert(new ComplaintRating { ComplaintId = complaintId, Rating = rating, IssueResolved = issueResolved, Comment = comment });
        else
        {
            existing.Rating = rating; existing.IssueResolved = issueResolved; existing.Comment = comment;
            await _db.From<ComplaintRating>().Update(existing);
        }
    }

    // ---------- appeals ----------

    public async Task AppealAsync(int studentId, int complaintId, string? reason)
    {
        reason = (reason ?? "").Trim();
        if (reason.Length < 10) throw new StudentApiException(400, "Please explain in at least 10 characters why this request should be reviewed.");
        if (reason.Length > 500) throw new StudentApiException(400, "Your explanation is too long (max 500 characters).");
        var c = await GetOwnComplaintAsync(studentId, complaintId);
        if (c.StatusId != StatusIds.Rejected) throw new StudentApiException(400, "Only rejected requests can be appealed.");
        if (c.AppealStatus is not null) throw new StudentApiException(409, "You have already appealed this decision.");

        c.AppealStatus = AppealStatuses.Pending;
        c.AppealReason = reason;
        c.AppealedAt = DateTime.UtcNow;
        await _db.From<Complaint>().Update(c);
        await _notify.NotifyAsync(studentId, complaintId, $"Your appeal for C{complaintId:D3} was sent to the manager. You will be told the decision here.");
    }

    // ---------- conversation with staff ----------

    public async Task<List<MessageDto>> GetMessagesAsync(int studentId, int complaintId)
    {
        await GetOwnComplaintAsync(studentId, complaintId);
        return (await _db.From<ComplaintMessage>().Filter("complaint_id", Operator.Equals, complaintId.ToString())
                .Order("created_at", Ordering.Ascending).Get()).Models
            .Select(m => new MessageDto(m.Id, m.Sender, m.SenderName, m.Body, m.PhotoUrl, m.CreatedAt.AsUtc(), m.ReadAt is not null)).ToList();
    }

    /// <summary>The conversation as the student sees it while it is open: opening it marks the staff messages as seen.</summary>
    public async Task<ChatDto> GetChatAsync(int studentId, int complaintId)
    {
        await GetOwnComplaintAsync(studentId, complaintId);
        var snap = await _chat.ThreadAsync(complaintId, ChatService.Student, markSeen: true);
        return new ChatDto(snap.Messages.Select(m => new MessageDto(m.Id, m.Sender, m.Name, m.Body, m.PhotoUrl, m.CreatedAt, m.Seen)).ToList(), snap.TypingName);
    }

    /// <summary>The student is typing in this conversation (shown to staff for a few seconds).</summary>
    public async Task TypingAsync(int studentId, int complaintId)
    {
        if (!_chat.AllowPing(complaintId, ChatService.Student)) return;
        var c = await GetOwnComplaintAsync(studentId, complaintId);
        if (c.StatusId is StatusIds.Submitted or StatusIds.InProgress) _chat.Typing(complaintId, ChatService.Student, "Student");
    }

    public async Task<MessageDto> PostMessageAsync(int studentId, int complaintId, string? body, byte[]? photo)
    {
        var c = await GetOwnComplaintAsync(studentId, complaintId);
        if (c.StatusId is not (StatusIds.Submitted or StatusIds.InProgress))
            throw new StudentApiException(400, "Messages can only be sent while a request is open.");
        body = (body ?? "").Trim();
        var hasPhoto = photo is { Length: > 0 };
        if (body.Length == 0 && !hasPhoto) throw new StudentApiException(400, "Write a message or attach a photo.");
        if (body.Length > 1000) throw new StudentApiException(400, "Your message is too long (max 1000 characters).");

        // Per-account limits: 12 messages an hour, and the same 5-photos-a-day budget as new requests.
        var mine = (await _db.From<Complaint>().Filter("student_id", Operator.Equals, studentId.ToString()).Get()).Models;
        var myIds = mine.Select(x => (object)x.ComplaintId).ToList();
        var now = DateTime.UtcNow;
        var recent = (await _db.From<ComplaintMessage>().Filter("complaint_id", Operator.In, myIds)
            .Filter("created_at", Operator.GreaterThanOrEqual, now.AddHours(-24).ToString("o")).Get()).Models;
        if (recent.Count(m => m.Sender == "student" && m.CreatedAt.AsUtc() >= now.AddHours(-1)) >= 12)
            throw new StudentApiException(429, "You are sending messages too quickly. Please wait a little and try again.");
        var maxPhotos = _config.GetValue("Student:MaxPhotosPerDay", 5);
        if (hasPhoto)
        {
            var photosToday = recent.Count(m => m.Sender == "student" && m.PhotoUrl != null)
                + mine.Count(x => x.CreatedAt.AsUtc() >= now.AddHours(-24) && (x.PhotoUrl != null || x.PhotoNote != null));
            if (photosToday >= maxPhotos)
                throw new StudentApiException(429, $"You have reached today's limit of {maxPhotos} photos. You can still send a text message.");
        }

        string? photoUrl = null, photoId = null;
        if (hasPhoto)
        {
            if (photo!.Length > MaxPhotoBytes) throw new StudentApiException(400, "Photo is too large (max 5 MB).");
            var ext = SniffImage(photo) ?? throw new StudentApiException(400, "Only JPG, PNG or WebP photos are allowed.");
            if (!_images.IsConfigured) throw new StudentApiException(503, "Photo upload is not available right now. Please send a text message.");
            try
            {
                var up = await _images.UploadAsync(photo, ext, studentId);
                photoUrl = up.Url; photoId = up.PublicId;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Message photo upload failed for student {StudentId}", studentId);
                throw new StudentApiException(502, "Could not upload the photo. Please try again or send a text message.");
            }

            // Every photo is checked before staff can see it.
            (bool Ok, string Reason) check;
            try { check = await _ai.CheckPhotoAsync(photoUrl, $"{c.Description}\n{body}"); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Photo safety check failed for student {StudentId}", studentId);
                await _images.DeleteAsync(photoId);
                throw new StudentApiException(503, "We could not check your photo right now. Please try again, or send a text message.");
            }
            if (!check.Ok)
            {
                await _images.DeleteAsync(photoId);
                throw new StudentApiException(400, "Your photo was not sent: " + (string.IsNullOrWhiteSpace(check.Reason) ? "it does not show the problem." : check.Reason));
            }
        }

        var student = await GetStudentAsync(studentId);
        var user = await GetUserAsync(student.UserId);
        try
        {
            var saved = (await _db.From<ComplaintMessage>().Insert(new ComplaintMessage
            {
                ComplaintId = complaintId, Sender = "student", SenderName = user.FullName,
                Body = body.Length == 0 ? "Sent a photo" : body, PhotoUrl = photoUrl
            })).Model ?? throw new StudentApiException(500, "Could not save your message.");
            _chat.StopTyping(complaintId, ChatService.Student);
            return new MessageDto(saved.Id, saved.Sender, saved.SenderName, saved.Body, saved.PhotoUrl, saved.CreatedAt.AsUtc());
        }
        catch
        {
            if (photoId is not null) await _images.DeleteAsync(photoId);
            throw;
        }
    }

    // ---------- browser push ----------

    public object GetPushKey() => new { enabled = _push.IsConfigured, key = _push.PublicKey };

    public async Task SubscribePushAsync(int studentId, string endpoint, string p256dh, string auth, string? userAgent)
    {
        if (!_push.IsConfigured) throw new StudentApiException(503, "Push notifications are not available right now.");
        await _push.SubscribeAsync(studentId, endpoint, p256dh, auth, userAgent);
    }

    public Task UnsubscribePushAsync(int studentId, string endpoint) => _push.UnsubscribeAsync(studentId, endpoint);

    /// <summary>Lets a student check that alerts reach this device. Limited to 5 per 15 minutes.</summary>
    public async Task<int> SendTestPushAsync(int studentId)
    {
        var key = $"pushtest|{studentId}";
        if (_throttle.IsLocked(key, out var left))
            throw new StudentApiException(429, $"Please wait {LoginThrottle.Minutes(left)} minute(s) before sending another test.");
        _throttle.Fail(key);
        if (!_push.IsConfigured) throw new StudentApiException(503, "Push notifications are not available right now.");
        var sent = await _push.SendAsync(studentId, "This is a test. Push notifications are working on this device.", null);
        if (sent == 0) throw new StudentApiException(409, "No device is subscribed yet. Turn on push notifications first.");
        return sent;
    }

    // ---------- SMS alert preference ----------

    public async Task SetSmsAlertsAsync(int studentId, bool enabled)
    {
        var s = await GetStudentAsync(studentId);
        s.SmsAlerts = enabled;
        await _db.From<Student>().Update(s);
    }
    // ---------- notifications ----------

    public async Task<List<NotificationDto>> GetNotificationsAsync(int studentId) =>
        (await _db.From<StudentNotification>().Filter("student_id", Operator.Equals, studentId.ToString())
            .Order("created_at", Ordering.Descending).Limit(30).Get()).Models
        .Select(n => new NotificationDto(n.Id, n.Message, n.IsRead, n.CreatedAt.AsUtc(), n.ComplaintId)).ToList();

    public async Task MarkNotificationReadAsync(int studentId, long notificationId)
    {
        var n = (await _db.From<StudentNotification>().Filter("id", Operator.Equals, notificationId.ToString()).Limit(1).Get()).Models.FirstOrDefault();
        if (n is null || n.StudentId != studentId) throw new StudentApiException(404, "Notification not found.");
        if (n.IsRead) return;
        n.IsRead = true;
        await _db.From<StudentNotification>().Update(n);
    }

    public async Task MarkNotificationsReadAsync(int studentId)
    {
        var unread = (await _db.From<StudentNotification>().Filter("student_id", Operator.Equals, studentId.ToString())
            .Filter("is_read", Operator.Equals, "false").Get()).Models;
        foreach (var n in unread) { n.IsRead = true; await _db.From<StudentNotification>().Update(n); }
    }
}
