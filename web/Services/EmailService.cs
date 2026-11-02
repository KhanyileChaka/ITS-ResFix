using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Sends email. Three ways, tried in this order, all configured through user-secrets / environment only:
///   1. Gmail API, over HTTPS (no domain needed, delivers to anyone): Gmail:ClientId, Gmail:ClientSecret, Gmail:RefreshToken, Gmail:Sender.
///      Get the refresh token once with tools/get-gmail-token.ps1.
///   2. Resend (https://resend.com), over HTTPS: Resend:ApiKey and Email:From. Without a verified domain it only delivers to its owner.
///   3. SMTP: Smtp:Host, Smtp:Port (default 587), Smtp:User, Smtp:Password, Smtp:From. Hosts such as Render's free plan block SMTP ports.
/// When none is set, <see cref="IsConfigured"/> is false and nothing is sent.
/// </summary>
public class EmailService
{
    private readonly HttpClient _http;
    private readonly ILogger<EmailService> _log;
    private readonly string _logoPath;
    private readonly string? _gmailId, _gmailSecret, _gmailRefresh, _gmailSender, _fromName;
    private readonly string? _resendKey, _resendFrom, _replyTo;
    // shared by every instance (the service is created per request), so one Google token is reused until it nearly expires
    private static string? _gmailAccessToken;
    private static DateTime _gmailTokenExpires = DateTime.MinValue;
    private static readonly SemaphoreSlim _gmailLock = new(1, 1);
    private readonly string? _host, _user, _password, _from;
    private readonly int _port;

    public EmailService(HttpClient http, IConfiguration config, ILogger<EmailService> log, IWebHostEnvironment env)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(25);
        _log = log;
        _logoPath = Path.Combine(env.WebRootPath ?? "", "images", "logo.jpg");
        _gmailId = config["Gmail:ClientId"];
        _gmailSecret = config["Gmail:ClientSecret"];
        _gmailRefresh = config["Gmail:RefreshToken"];
        _gmailSender = config["Gmail:Sender"];
        _fromName = string.IsNullOrWhiteSpace(config["Email:FromName"]) ? "ITS ResFix" : config["Email:FromName"]!.Trim();
        _replyTo = config["Email:ReplyTo"];
        _resendKey = config["Resend:ApiKey"];
        _resendFrom = config["Email:From"];
        _host = config["Smtp:Host"];
        _user = config["Smtp:User"];
        _password = config["Smtp:Password"];
        _from = config["Smtp:From"];
        _port = int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
    }

    private bool UsesGmail => !string.IsNullOrWhiteSpace(_gmailId) && !string.IsNullOrWhiteSpace(_gmailSecret)
        && !string.IsNullOrWhiteSpace(_gmailRefresh) && !string.IsNullOrWhiteSpace(_gmailSender);
    private bool UsesResend => !UsesGmail && !string.IsNullOrWhiteSpace(_resendKey);

    /// <summary>The sender address shown to people.</summary>
    public string? FromAddress => UsesGmail ? _gmailSender : UsesResend ? (string.IsNullOrWhiteSpace(_resendFrom) ? "ITS ResFix <onboarding@resend.dev>" : _resendFrom) : _from;

    public bool IsConfigured => UsesGmail || UsesResend
        || (!string.IsNullOrWhiteSpace(_host) && !string.IsNullOrWhiteSpace(_from)
            && (string.IsNullOrWhiteSpace(_user) || !string.IsNullOrWhiteSpace(_password)));

    /// <summary>True when the email was accepted. Never throws: a failed email must not break a request.</summary>
    public async Task<bool> SendAsync(string to, string subject, string body, string? html = null) => (await TrySendAsync(to, subject, body, html)).Ok;

    /// <summary>Like <see cref="SendAsync"/> but also says, in plain words, why a send failed.</summary>
    public async Task<(bool Ok, string? Problem)> TrySendAsync(string to, string subject, string body, string? html = null)
    {
        if (!IsConfigured) return (false, "Email is not set up on the server.");
        subject = subject.Replace('\r', ' ').Replace('\n', ' '); // no header injection
        if (UsesGmail) return await SendWithGmailAsync(to, subject, body, html);
        return UsesResend ? await SendWithResendAsync(to, subject, body, html) : await SendWithSmtpAsync(to, subject, body, html);
    }

    // ---------- Gmail API (HTTPS, no domain needed) ----------

    /// <summary>A short-lived access token, fetched with the long-lived refresh token and reused until it nearly expires.</summary>
    private async Task<string?> GmailAccessTokenAsync()
    {
        await _gmailLock.WaitAsync();
        try
        {
            if (_gmailAccessToken is not null && DateTime.UtcNow < _gmailTokenExpires) return _gmailAccessToken;
            using var resp = await _http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _gmailId!, ["client_secret"] = _gmailSecret!, ["refresh_token"] = _gmailRefresh!, ["grant_type"] = "refresh_token"
            }));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Google refused the saved Gmail sign-in ({Status}): {Body}", (int)resp.StatusCode, body);
                return null;
            }
            using var doc = JsonDocument.Parse(body);
            _gmailAccessToken = doc.RootElement.GetProperty("access_token").GetString();
            var seconds = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;
            _gmailTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 120));
            return _gmailAccessToken;
        }
        finally { _gmailLock.Release(); }
    }

    private async Task<(bool Ok, string? Problem)> SendWithGmailAsync(string to, string subject, string text, string? html)
    {
        try
        {
            to = new MailAddress(to).Address; // refuses anything that is not a plain address
            var token = await GmailAccessTokenAsync();
            if (token is null) return (false, "Google refused the saved Gmail sign-in. Run tools/get-gmail-token.ps1 again to renew it.");

            byte[]? logo = html is not null && File.Exists(_logoPath) ? await File.ReadAllBytesAsync(_logoPath) : null;
            var raw = MimeBuilder.ToBase64Url(MimeBuilder.Build(_fromName!, _gmailSender!, to, subject, text, html, logo, _replyTo));

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { raw }), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return (true, null);

            var detail = await resp.Content.ReadAsStringAsync();
            _log.LogWarning("Gmail rejected an email to {To} ({Status}): {Detail}", to, (int)resp.StatusCode, detail);
            if ((int)resp.StatusCode == 401) _gmailAccessToken = null; // fetch a fresh token next time
            return (false, (int)resp.StatusCode switch
            {
                401 => "Gmail refused the sign-in. Run tools/get-gmail-token.ps1 again.",
                403 => "Gmail would not let this app send (is the Gmail API switched on, and the gmail.send permission granted?).",
                429 => "Gmail is limiting how many emails can be sent right now.",
                _ => $"Gmail could not send the message ({(int)resp.StatusCode})."
            });
        }
        catch (FormatException)
        {
            return (false, "That email address is not valid.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Could not reach Gmail");
            return (false, "Could not reach Gmail.");
        }
    }

    // ---------- Resend (HTTPS) ----------

    private async Task<(bool Ok, string? Problem)> SendWithResendAsync(string to, string subject, string text, string? html)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["from"] = FromAddress,
                ["to"] = new[] { to },
                ["subject"] = subject,
                ["text"] = text
            };
            if (!string.IsNullOrWhiteSpace(_replyTo)) payload["reply_to"] = _replyTo; // replies go to a real inbox
            if (html is not null)
            {
                payload["html"] = html;
                if (File.Exists(_logoPath)) // the logo is embedded, referenced in the HTML as cid:logo
                    payload["attachments"] = new[]
                    {
                        new Dictionary<string, object> { ["filename"] = "logo.jpg", ["content"] = Convert.ToBase64String(await File.ReadAllBytesAsync(_logoPath)), ["content_id"] = "logo" }
                    };
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendKey);
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return (true, null);

            var detail = await resp.Content.ReadAsStringAsync();
            _log.LogWarning("Resend rejected an email to {To} ({Status}): {Detail}", to, (int)resp.StatusCode, detail);
            return (false, (int)resp.StatusCode switch
            {
                401 or 403 when detail.Contains("verify", StringComparison.OrdinalIgnoreCase) || detail.Contains("testing", StringComparison.OrdinalIgnoreCase)
                    => "Resend only delivers to your own address until a sending domain is verified.",
                401 => "Resend refused the API key.",
                403 => "Resend refused the message (check the sender address and domain).",
                422 => "Resend did not accept the message (check the sender and recipient addresses).",
                429 => "Resend is limiting how many emails can be sent right now.",
                _ => $"Resend could not send the message ({(int)resp.StatusCode})."
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Could not reach Resend");
            return (false, "Could not reach the email service.");
        }
    }

    // ---------- SMTP (kept as a fallback) ----------

    private async Task<(bool Ok, string? Problem)> SendWithSmtpAsync(string to, string subject, string body, string? html)
    {
        try
        {
            using var message = new MailMessage(new MailAddress(_from!), new MailAddress(to)) { Subject = subject, Body = body, IsBodyHtml = false };
            if (!string.IsNullOrWhiteSpace(_replyTo)) message.ReplyToList.Add(new MailAddress(_replyTo));
            if (html is not null)
            {
                var view = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html");
                if (File.Exists(_logoPath))
                    view.LinkedResources.Add(new LinkedResource(_logoPath, "image/jpeg") { ContentId = "logo" });
                message.AlternateViews.Add(view);
            }
            using var client = new SmtpClient(_host!, _port) { EnableSsl = true, Timeout = 20000 };
            if (!string.IsNullOrWhiteSpace(_user)) client.Credentials = new NetworkCredential(_user, _password);
            await client.SendMailAsync(message);
            return (true, null);
        }
        catch (SmtpException ex)
        {
            _log.LogWarning("Could not send email to {To}: {Status} {Message}", to, ex.StatusCode, ex.Message);
            var login = ex.StatusCode is SmtpStatusCode.MustIssueStartTlsFirst or SmtpStatusCode.ClientNotPermitted
                || ex.Message.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("5.7.", StringComparison.Ordinal)
                || ex.Message.Contains("protocol violation", StringComparison.OrdinalIgnoreCase);
            return (false, login
                ? "The mail server refused the sign-in. Gmail needs an App Password (not the normal account password)."
                : $"The mail server rejected the message ({ex.StatusCode}).");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not send email to {To}", to);
            return (false, "Could not reach the mail server.");
        }
    }
}
