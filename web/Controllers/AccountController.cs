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

/// <summary>Single login for managers and internal workers, with role-based redirect.</summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private const string InvalidCredentials = "Invalid email or password.";

    private readonly ResFixDataService _data;
    private readonly ILogger<AccountController> _logger;
    private readonly LoginThrottle _throttle;
    private readonly PasswordResetService _reset;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly IServiceScopeFactory _scopes;

    public AccountController(ResFixDataService data, ILogger<AccountController> logger, LoginThrottle throttle,
        PasswordResetService reset, IConfiguration config, IWebHostEnvironment env, IServiceScopeFactory scopes)
    {
        _scopes = scopes;
        _data = data;
        _logger = logger;
        _throttle = throttle;
        _reset = reset;
        _config = config;
        _env = env;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToDashboard(User.FindFirstValue(ClaimTypes.Role));

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var emailKey = model.Email.Trim().ToLowerInvariant();
        var throttleKey = $"staff|{HttpContext.Connection.RemoteIpAddress}|{emailKey}";
        var acctKey = $"staff-acct|{emailKey}";   // caps guessing spread over many addresses
        if (_throttle.IsLocked(throttleKey, out var wait) || _throttle.IsLocked(acctKey, out wait))
        {
            ModelState.AddModelError(string.Empty, $"Too many failed attempts. Try again in {LoginThrottle.Minutes(wait)} minute(s).");
            return View(model);
        }

        var user = await _data.GetUserForLoginAsync(model.Email);
        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
        {
            _throttle.Fail(throttleKey);
            _throttle.Fail(acctKey, 20);
            ModelState.AddModelError(string.Empty, InvalidCredentials);
            return View(model);
        }
        _throttle.Reset(throttleKey);
        _throttle.Reset(acctKey);

        if (user.Role == Roles.Student)
        {
            ModelState.AddModelError(string.Empty, "Students cannot sign in here. Please use the ITS ResFix mobile app.");
            return View(model);
        }

        if (user.AccountStatus != AccountStatuses.Active)
        {
            ModelState.AddModelError(string.Empty, user.AccountStatus == AccountStatuses.Suspended
                ? "Your account is suspended. Please contact the residence manager."
                : "This account is no longer active. Please contact the residence manager.");
            return View(model);
        }

        if (user.Role is not (Roles.Manager or Roles.Worker))
        {
            _logger.LogWarning("Login refused for user {UserId}: unrecognised role '{Role}'", user.UserId, user.Role);
            ModelState.AddModelError(string.Empty, "Your account does not have staff access.");
            return View(model);
        }

        int? workerId = null;
        if (user.Role == Roles.Worker)
        {
            var worker = await _data.GetWorkerByUserIdAsync(user.UserId);
            if (worker is null)
            {
                ModelState.AddModelError(string.Empty, "Your account is not linked to an internal worker profile. Contact the residence manager.");
                return View(model);
            }
            workerId = worker.WorkerId;
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffPrincipal.Create(user, workerId));

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return LocalRedirect(model.ReturnUrl);

        return RedirectToDashboard(user.Role);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var id = model.Identifier.Trim();
        var isEmail = id.Contains('@');
        var valid = isEmail
            ? new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(id)
            : System.Text.RegularExpressions.Regex.IsMatch(id, @"^(\+27|0)[\d ]{9,12}$");
        if (!valid)
        {
            ModelState.AddModelError(nameof(model.Identifier), "Enter a valid email address or an SA cellphone number like 071 234 5678.");
            return View(model);
        }

        // Limits stop anyone from using this page to message a stranger repeatedly or to flood us.
        var phoneKey = isEmail ? $"forgot-email|{id.ToLowerInvariant()}" : $"forgot-phone|{SmsService.ToE164(id)}";
        var ipKey = $"forgot-ip|{HttpContext.Connection.RemoteIpAddress}";
        var limited = _throttle.IsLocked(phoneKey, out _) || _throttle.IsLocked(ipKey, out _);
        _throttle.Fail(phoneKey, 3);
        _throttle.Fail(ipKey, 10);

        var baseUrl = _config["Hosting:PublicUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) && _env.IsDevelopment()) baseUrl = $"{Request.Scheme}://{Request.Host}";
        if (string.IsNullOrWhiteSpace(baseUrl))
            _logger.LogError("Hosting:PublicUrl is not set, so password reset links cannot be sent.");
        else if (!limited)
        {
            // Done in the background so the page answers equally fast whether or not the account exists
            // (sending an email takes seconds, which would otherwise reveal that the account is real).
            _ = Task.Run(async () =>
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PasswordResetService>().RequestAsync(id, baseUrl);
            });
        }

        // Same answer whether or not the address or number matched, so this page cannot be used to find staff accounts.
        ViewBag.Sent = true;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        if (!await _reset.IsValidAsync(token)) { ViewBag.Invalid = true; return View(new ResetPasswordViewModel()); }
        return View(new ResetPasswordViewModel { Token = token! });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (!await _reset.CompleteAsync(model.Token, model.NewPassword))
        {
            ViewBag.Invalid = true;
            return View(new ResetPasswordViewModel());
        }
        TempData["Success"] = "Your password has been changed. Sign in with the new password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    [HttpGet]
    public IActionResult Error() => View();

    /// <summary>Public privacy policy (linked from the Google sign-in screen and the site footer).</summary>
    [HttpGet("/Privacy")]
    public IActionResult Privacy() => View();

    private IActionResult RedirectToDashboard(string? role) => role switch
    {
        Roles.Manager => RedirectToAction("Dashboard", "Manager"),
        Roles.Worker => RedirectToAction("Dashboard", "Worker"),
        _ => RedirectToAction(nameof(AccessDenied))
    };
}
