using System.ComponentModel.DataAnnotations;
using ITSResFix.Staff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ITSResFix.Staff.Controllers;

public record CellphoneRequest([Required, StringLength(20)] string Cellphone);
public record LoginRequest([Required, StringLength(20)] string Cellphone, [Required, StringLength(100)] string Password);
public record VerifyOtpRequest([Required, StringLength(20)] string Cellphone, [Required, StringLength(10)] string Code);
public record NewPasswordRequest([Required, StringLength(100)] string NewPassword);
public record ChangePasswordRequest([Required, StringLength(100)] string Current, [Required, StringLength(100)] string NewPassword);
public record RatingRequest([Range(1, 5)] int Rating, bool IssueResolved, [StringLength(500)] string? Comment);

public record AppealRequest([Required, StringLength(500)] string Reason);
public record SmsAlertsRequest(bool Enabled);
public record PushSubscribeRequest([Required, StringLength(600)] string Endpoint, [Required, StringLength(200)] string P256dh, [Required, StringLength(100)] string Auth);
public record PushEndpointRequest([Required, StringLength(600)] string Endpoint);

public class MessageForm
{
    [StringLength(1000)] public string? Body { get; set; }
    public IFormFile? Photo { get; set; }
}
public class SubmitComplaintForm
{
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    [Required, StringLength(1000)] public string Description { get; set; } = "";
    public bool IsEmergency { get; set; }
    public IFormFile? Photo { get; set; }
}

/// <summary>JSON API for the student mobile app. Bearer-token auth (not cookies), so CSRF does not apply.</summary>
[ApiController]
[Route("api/student")]
[EnableCors("student-app")]
public class StudentApiController : ControllerBase
{
    private readonly StudentApiService _svc;
    private readonly ILogger<StudentApiController> _log;

    public StudentApiController(StudentApiService svc, ILogger<StudentApiController> log)
    {
        _svc = svc; _log = log;
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private int StudentId => int.Parse(User.FindFirst("studentId")!.Value);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (StudentApiException ex) { return StatusCode(ex.Status, new { error = ex.Message }); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Student API failure on {Path}", Request.Path);
            return StatusCode(500, new { error = "Something went wrong. Please try again." });
        }
    }

    // ---------- anonymous auth endpoints (rate limited) ----------

    [HttpPost("auth/check"), AllowAnonymous]
    public Task<IActionResult> Check(CellphoneRequest r) =>
        Run(async () => Ok(new { registered = await _svc.IsRegisteredAsync(r.Cellphone) }));

    [HttpPost("auth/login"), AllowAnonymous]
    public Task<IActionResult> Login(LoginRequest r) =>
        Run(async () => Ok(await _svc.LoginAsync(r.Cellphone, r.Password, Ip)));

    [HttpPost("auth/otp/request"), AllowAnonymous]
    public Task<IActionResult> RequestOtp(CellphoneRequest r) =>
        Run(async () => { await _svc.RequestOtpAsync(r.Cellphone, Ip); return Ok(new { sent = true }); });

    [HttpPost("auth/otp/verify"), AllowAnonymous]
    public Task<IActionResult> VerifyOtp(VerifyOtpRequest r) =>
        Run(async () => Ok(new { setupToken = await _svc.VerifyOtpAsync(r.Cellphone, r.Code, Ip) }));

    [HttpPost("auth/forgot/request"), AllowAnonymous]
    public Task<IActionResult> ForgotRequest(CellphoneRequest r) =>
        Run(async () => { await _svc.RequestResetAsync(r.Cellphone, Ip); return Ok(new { sent = true }); });

    [HttpPost("auth/forgot/verify"), AllowAnonymous]
    public Task<IActionResult> ForgotVerify(VerifyOtpRequest r) =>
        Run(async () => Ok(new { resetToken = await _svc.VerifyResetAsync(r.Cellphone, r.Code, Ip) }));

    [HttpPost("auth/password/reset"), Authorize(Policy = "StudentPasswordReset")]
    public Task<IActionResult> ResetPassword(NewPasswordRequest r) =>
        Run(async () => { await _svc.ResetPasswordAsync(StudentId, r.NewPassword, User.FindFirst("jti")?.Value); return Ok(new { changed = true }); });

    [HttpPost("auth/password/set"), Authorize(Policy = "StudentPasswordSetup")]
    public Task<IActionResult> SetPassword(NewPasswordRequest r) =>
        Run(async () => Ok(await _svc.SetPasswordAsync(StudentId, r.NewPassword)));

    // ---------- authenticated endpoints ----------

    [HttpGet("me"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Me() => Run(async () => Ok(await _svc.GetMeAsync(StudentId)));

    [HttpPost("password/change"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> ChangePassword(ChangePasswordRequest r) =>
        Run(async () => { await _svc.ChangePasswordAsync(StudentId, r.Current, r.NewPassword, Ip); return Ok(new { changed = true }); });

    [HttpGet("categories"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Categories() => Run(async () => Ok(await _svc.GetCategoriesAsync()));

    [HttpGet("complaints"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Complaints() => Run(async () => Ok(await _svc.GetComplaintsAsync(StudentId)));

    [HttpPost("complaints"), Authorize(Policy = "StudentSession"), EnableRateLimiting("submit")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> Submit([FromForm] SubmitComplaintForm form) =>
        Run(async () =>
        {
            byte[]? bytes = null;
            if (form.Photo is { Length: > 0 })
            {
                if (form.Photo.Length > 5 * 1024 * 1024) return BadRequest(new { error = "Photo is too large (max 5 MB)." });
                using var ms = new MemoryStream();
                await form.Photo.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            var id = await _svc.SubmitComplaintAsync(StudentId, form.CategoryId, form.Description, form.IsEmergency, bytes);
            return Ok(new { complaintId = id });
        });

    [HttpGet("complaints/{id:int}/review"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Review(int id) => Run(async () => Ok(await _svc.GetReviewAsync(StudentId, id)));

    [HttpPost("complaints/{id:int}/rating"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Rate(int id, RatingRequest r) =>
        Run(async () => { await _svc.SubmitRatingAsync(StudentId, id, r.Rating, r.IssueResolved, r.Comment); return Ok(new { saved = true }); });

    [HttpPost("complaints/{id:int}/appeal"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Appeal(int id, AppealRequest r) =>
        Run(async () => { await _svc.AppealAsync(StudentId, id, r.Reason); return Ok(new { appealed = true }); });

    [HttpGet("complaints/{id:int}/messages"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Messages(int id) => Run(async () => Ok(await _svc.GetMessagesAsync(StudentId, id)));

    [HttpGet("complaints/{id:int}/chat"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Chat(int id) => Run(async () => Ok(await _svc.GetChatAsync(StudentId, id)));

    [HttpPost("complaints/{id:int}/typing"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Typing(int id) => Run(async () => { await _svc.TypingAsync(StudentId, id); return Ok(new { ok = true }); });

    [HttpPost("complaints/{id:int}/messages"), Authorize(Policy = "StudentSession")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> PostMessage(int id, [FromForm] MessageForm form) =>
        Run(async () =>
        {
            byte[]? bytes = null;
            if (form.Photo is { Length: > 0 })
            {
                if (form.Photo.Length > 5 * 1024 * 1024) return BadRequest(new { error = "Photo is too large (max 5 MB)." });
                using var ms = new MemoryStream();
                await form.Photo.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            return Ok(await _svc.PostMessageAsync(StudentId, id, form.Body, bytes));
        });

    [HttpGet("push/key"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> PushKey() => Run(() => Task.FromResult<IActionResult>(Ok(_svc.GetPushKey())));

    [HttpPost("push/subscribe"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> PushSubscribe(PushSubscribeRequest r) =>
        Run(async () => { await _svc.SubscribePushAsync(StudentId, r.Endpoint, r.P256dh, r.Auth, Request.Headers.UserAgent.ToString()); return Ok(new { subscribed = true }); });

    [HttpPost("push/unsubscribe"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> PushUnsubscribe(PushEndpointRequest r) =>
        Run(async () => { await _svc.UnsubscribePushAsync(StudentId, r.Endpoint); return Ok(new { subscribed = false }); });

    [HttpPost("push/test"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> PushTest() => Run(async () => Ok(new { sent = await _svc.SendTestPushAsync(StudentId) }));

    [HttpPost("sms-alerts"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> SmsAlerts(SmsAlertsRequest r) =>
        Run(async () => { await _svc.SetSmsAlertsAsync(StudentId, r.Enabled); return Ok(new { enabled = r.Enabled }); });

    [HttpGet("notifications"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> Notifications() => Run(async () => Ok(await _svc.GetNotificationsAsync(StudentId)));

    [HttpPost("notifications/{id:long}/read"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> MarkOneRead(long id) => Run(async () => { await _svc.MarkNotificationReadAsync(StudentId, id); return Ok(new { ok = true }); });

    [HttpPost("notifications/read"), Authorize(Policy = "StudentSession")]
    public Task<IActionResult> MarkRead() => Run(async () => { await _svc.MarkNotificationsReadAsync(StudentId); return Ok(new { ok = true }); });
}
