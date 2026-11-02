using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Sends SMS through the BulkSMS REST API (https://www.bulksms.com). Credentials live in user-secrets only:
///   Sms:BulkSmsTokenId and Sms:BulkSmsTokenSecret (an API token from your BulkSMS account),
///   optional Sms:From (an approved sender name or number).
/// When the token is missing, <see cref="IsConfigured"/> is false and nothing is sent.
/// </summary>
public class SmsService
{
    private readonly HttpClient _http;
    private readonly ILogger<SmsService> _log;
    private readonly string? _tokenId, _tokenSecret, _from;

    public SmsService(HttpClient http, IConfiguration config, ILogger<SmsService> log)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(20);
        _log = log;
        _tokenId = config["Sms:BulkSmsTokenId"];
        _tokenSecret = config["Sms:BulkSmsTokenSecret"];
        _from = config["Sms:From"];
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_tokenId) && !string.IsNullOrWhiteSpace(_tokenSecret);

    /// <summary>South African numbers (0821234567, 27821234567) become +27821234567.</summary>
    public static string ToE164(string phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0") && digits.Length == 10) return "+27" + digits[1..];
        if (digits.StartsWith("27") && digits.Length == 11) return "+" + digits;
        return "+" + digits;
    }

    /// <summary>Returns true when BulkSMS accepted the message. Never throws: a failed SMS must not break a request.</summary>
    public async Task<bool> SendAsync(string to, string body, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            if (body.Length > 320) body = body[..317] + "...";
            var payload = new Dictionary<string, object> { ["to"] = ToE164(to), ["body"] = body };
            if (!string.IsNullOrWhiteSpace(_from)) payload["from"] = _from!;

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.bulksms.com/v1/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_tokenId}:{_tokenSecret}")));
            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return true;
            _log.LogWarning("BulkSMS rejected an SMS ({Status}): {Body}", (int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "SMS send failed");
            return false;
        }
    }
}
