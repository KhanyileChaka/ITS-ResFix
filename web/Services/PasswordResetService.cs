using System.Security.Cryptography;
using System.Text;
using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models;
using ITSResFix.Staff.Models.Db;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Staff "forgot password": the person enters the cellphone number on their account and gets an SMS with a
/// single-use link (valid 30 minutes). Only a SHA-256 hash of the token is stored.
/// </summary>
public class PasswordResetService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly Supabase.Client _db;
    private readonly ResFixDataService _data;
    private readonly SmsService _sms;
    private readonly EmailService _email;
    private readonly StaffSessionValidator _sessions;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PasswordResetService> _log;

    private readonly StudentSessionValidator _studentSessions;

    public PasswordResetService(Supabase.Client db, ResFixDataService data, SmsService sms, EmailService email, StaffSessionValidator sessions,
        StudentSessionValidator studentSessions, IWebHostEnvironment env, ILogger<PasswordResetService> log)
    {
        _db = db; _data = data; _sms = sms; _email = email; _sessions = sessions; _studentSessions = studentSessions; _env = env; _log = log;
    }

    /// <summary>
    /// Sends a reset link when the email address or cellphone number belongs to a staff account: by email when the account
    /// has a real address and email is set up, and by SMS when a phone is known and SMS is set up. Silent either way.
    /// </summary>
    public async Task RequestAsync(string identifier, string baseUrl)
    {
        try
        {
            var user = identifier.Contains('@') ? await FindUserByEmailAsync(identifier) : await FindUserByPhoneAsync(identifier);
            if (user is null) return;

            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            await _db.From<PasswordResetRow>().Insert(new PasswordResetRow
            {
                UserId = user.UserId,
                TokenHash = Hash(token),
                ExpiresAt = DateTime.UtcNow.Add(Lifetime)
            });
            var link = $"{baseUrl.TrimEnd('/')}/Account/ResetPassword?token={token}";

            var sent = false;
            if (CanReceiveEmail(user.Email) && _email.IsConfigured)
            {
                var (html, text) = EmailTemplates.PasswordReset(user.DisplayName(), link);
                sent |= await _email.SendAsync(user.Email, "Reset your ITS ResFix password", text, html);
            }
            var phone = identifier.Contains('@') ? await PhoneOfAsync(user) : identifier;
            if (!string.IsNullOrWhiteSpace(phone) && _sms.IsConfigured)
                sent |= await _sms.SendAsync(phone, $"ITS ResFix: reset your password with this link (valid 30 minutes): {link}");

            if (!sent && _env.IsDevelopment())
                _log.LogInformation("DEV ONLY: no email or SMS was sent. Password reset link for user {UserId}: {Link}", user.UserId, link);
            else if (!sent)
                _log.LogWarning("Could not send a password reset message for user {UserId}", user.UserId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Password reset request failed");
        }
    }

    public record WelcomeResult(bool Emailed, bool Texted, string? Note);

    /// <summary>
    /// Tells a new worker their account exists: an email (sign-in address, login email and a 48-hour "choose your password" link)
    /// and an SMS pointing them at it. The password itself is never sent.
    /// </summary>
    public async Task<WelcomeResult> WelcomeWorkerAsync(AppUser user, string? phone, string baseUrl)
    {
        try
        {
            // a new welcome replaces any earlier link that has not been used
            var older = (await _db.From<PasswordResetRow>()
                .Filter("user_id", Operator.Equals, user.UserId.ToString())
                .Filter("used_at", Operator.Is, "null").Get()).Models;
            foreach (var o in older) { o.UsedAt = DateTime.UtcNow; await _db.From<PasswordResetRow>().Update(o); }

            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            await _db.From<PasswordResetRow>().Insert(new PasswordResetRow
            {
                UserId = user.UserId,
                TokenHash = Hash(token),
                ExpiresAt = DateTime.UtcNow.AddHours(48)
            });
            var root = baseUrl.TrimEnd('/');
            var setLink = $"{root}/Account/ResetPassword?token={token}";
            var signIn = $"{root}/Account/Login";

            bool emailed = false, texted = false;
            string? note = null;
            if (!CanReceiveEmail(user.Email)) note = "their email address cannot receive mail";
            else if (!_email.IsConfigured) note = "email is not set up on the server";
            else
            {
                var (html, text) = EmailTemplates.WorkerWelcome(user.DisplayName(), user.Email, signIn, setLink);
                emailed = await _email.SendAsync(user.Email, "Welcome to ITS ResFix: your staff account is ready", text, html);
                if (!emailed) note = "the email could not be sent";
            }
            if (!string.IsNullOrWhiteSpace(phone) && _sms.IsConfigured)
                texted = await _sms.SendAsync(phone, $"ITS ResFix: hi {user.DisplayName().Split(' ')[0]}, your worker account is ready. Sign in at {signIn} with {user.Email}. Choose your password using the link we emailed you.");

            if (!emailed && _env.IsDevelopment())
                _log.LogInformation("DEV ONLY: welcome email not sent ({Note}). Set-password link for user {UserId}: {Link}", note, user.UserId, setLink);
            return new WelcomeResult(emailed, texted, note);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not send the welcome messages for user {UserId}", user.UserId);
            return new WelcomeResult(false, false, "something went wrong");
        }
    }

    /// <summary>Seeded accounts use made-up .local addresses, which cannot receive mail.</summary>
    private static bool CanReceiveEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@') && !email.EndsWith(".local", StringComparison.OrdinalIgnoreCase);

    private async Task<AppUser?> FindUserByEmailAsync(string email)
    {
        var user = await _data.GetUserForLoginAsync(email);
        return user is not null && user.Role is Roles.Manager or Roles.Worker && user.AccountStatus == AccountStatuses.Active ? user : null;
    }

    private async Task<string?> PhoneOfAsync(AppUser user)
    {
        var worker = user.Role == Roles.Worker ? await _data.GetWorkerByUserIdAsync(user.UserId) : null;
        return !string.IsNullOrWhiteSpace(worker?.Phone) ? worker!.Phone : user.Cellphone;
    }

    /// <summary>
    /// Cancels everything an account has handed out: unused links stop working, the password becomes unusable (so any
    /// signed-in session ends) and the account must go through set-up again. Saves the user, including any other field
    /// the caller changed on it first (for example a corrected email).
    /// </summary>
    public async Task RevokeAccessAsync(AppUser user)
    {
        var open = (await _db.From<PasswordResetRow>()
            .Filter("user_id", Operator.Equals, user.UserId.ToString())
            .Filter("used_at", Operator.Is, "null").Get()).Models;
        foreach (var o in open) { o.UsedAt = DateTime.UtcNow; await _db.From<PasswordResetRow>().Update(o); }

        user.PasswordHash = PasswordHasher.Hash(Base64Url(RandomNumberGenerator.GetBytes(32)));
        user.MustChangePassword = true;
        await _data.UpdateUserAsync(user);

        // a dated marker: student tokens issued before it are refused (see StudentSessionValidator)
        await _db.From<PasswordResetRow>().Insert(new PasswordResetRow
        {
            UserId = user.UserId,
            TokenHash = StudentSessionValidator.RevokedPrefix + Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow,
            UsedAt = DateTime.UtcNow
        });
        _sessions.Invalidate(user.UserId);
        _studentSessions.Invalidate(user.UserId);
    }

    /// <summary>Puts a student back to "pending first login": no password, no session, no unused code. Their record is kept.</summary>
    public async Task CancelStudentSignupAsync(Student student, AppUser user, string? alsoPhone = null)
    {
        foreach (var phone in new[] { student.Cellphone, alsoPhone }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            var codes = (await _db.From<StudentOtp>().Filter("cellphone", Operator.Equals, phone!)
                .Filter("used", Operator.Equals, "false").Get()).Models;
            foreach (var c in codes) { c.Used = true; await _db.From<StudentOtp>().Update(c); }
        }

        await RevokeAccessAsync(user);
        user.MustChangePassword = false; // students have no must-change step; they are simply not registered
        await _data.UpdateUserAsync(user);
        student.IsRegistered = false;
        await _db.From<Student>().Update(student);
        _studentSessions.Invalidate(user.UserId);
    }

    /// <summary>Ends every session this person has right now (staff cookie checks and student tokens), without touching their password.</summary>
    public async Task EndSessionsAsync(int userId)
    {
        await _db.From<PasswordResetRow>().Insert(new PasswordResetRow
        {
            UserId = userId,
            TokenHash = StudentSessionValidator.RevokedPrefix + Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow,
            UsedAt = DateTime.UtcNow
        });
        _sessions.Invalidate(userId);
        _studentSessions.Invalidate(userId);
    }

    public async Task<bool> IsValidAsync(string? token)
    {
        var row = await FindOpenAsync(token);
        if (row is null) return false;
        var user = await _data.GetUserByIdAsync(row.UserId);
        return user is { AccountStatus: AccountStatuses.Active };
    }

    /// <summary>Sets the new password if the token is still good, then burns every outstanding token for that user.</summary>
    public async Task<bool> CompleteAsync(string? token, string newPassword)
    {
        var row = await FindOpenAsync(token);
        if (row is null) return false;
        var user = await _data.GetUserByIdAsync(row.UserId);
        if (user is null || user.AccountStatus != AccountStatuses.Active) return false;

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        await _data.UpdateUserAsync(user);

        var open = (await _db.From<PasswordResetRow>()
            .Filter("user_id", Operator.Equals, user.UserId.ToString())
            .Filter("used_at", Operator.Is, "null")
            .Get()).Models;
        foreach (var r in open)
        {
            r.UsedAt = DateTime.UtcNow;
            await _db.From<PasswordResetRow>().Update(r);
        }
        _sessions.Invalidate(user.UserId); // old sessions end: the password fingerprint changed
        return true;
    }

    private async Task<PasswordResetRow?> FindOpenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var row = (await _db.From<PasswordResetRow>()
            .Filter("token_hash", Operator.Equals, Hash(token))
            .Limit(1).Get()).Models.FirstOrDefault();
        return row is null || row.UsedAt is not null || row.ExpiresAt.AsUtc() < DateTime.UtcNow ? null : row;
    }

    private async Task<AppUser?> FindUserByPhoneAsync(string cellphone)
    {
        var target = SmsService.ToE164(cellphone);
        var workers = (await _db.From<InternalWorker>().Get()).Models;
        var worker = workers.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.Phone) && SmsService.ToE164(w.Phone!) == target);
        if (worker is not null) { var wu = await _data.GetUserByIdAsync(worker.UserId); return wu is { AccountStatus: AccountStatuses.Active } ? wu : null; }

        var users = (await _db.From<AppUser>().Get()).Models;
        return users.FirstOrDefault(u => (u.Role is Roles.Manager or Roles.Worker) && u.AccountStatus == AccountStatuses.Active
            && !string.IsNullOrWhiteSpace(u.Cellphone) && SmsService.ToE164(u.Cellphone!) == target);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
