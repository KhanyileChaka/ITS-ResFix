using System.Text.Json;
using ITSResFix.Staff.Models.Db;
using WebPush;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Self-hosted browser push (Web Push / VAPID). No Firebase account: our server signs each message and hands it to the
/// browser vendor's push relay. Keys live in user-secrets / environment: Push:PublicKey, Push:PrivateKey, Push:Subject.
/// Subscriptions are stored in Supabase (push_subscription).
/// </summary>
public class PushService
{
    // The subscription "endpoint" is a URL the browser gives us and we later POST to. Only accept real push relays,
    // otherwise a student could point the server at any address (SSRF).
    private static readonly string[] AllowedHosts =
    {
        "fcm.googleapis.com",                 // Chrome, Edge, Brave, Android browsers
        "updates.push.services.mozilla.com",  // Firefox
        "push.services.mozilla.com",
        "notify.windows.com",                 // legacy Edge
        "push.apple.com"                      // Safari (web.push.apple.com)
    };
    private const int MaxSubscriptionsPerStudent = 5;

    private readonly Supabase.Client _db;
    private readonly ILogger<PushService> _log;
    private readonly WebPushClient _client = new();
    private readonly VapidDetails? _vapid;

    public PushService(Supabase.Client db, IConfiguration config, ILogger<PushService> log)
    {
        _db = db; _log = log;
        var pub = config["Push:PublicKey"]; var priv = config["Push:PrivateKey"];
        var subject = config["Push:Subject"] ?? "mailto:admin@itsresfix.local";
        if (!string.IsNullOrWhiteSpace(pub) && !string.IsNullOrWhiteSpace(priv))
            _vapid = new VapidDetails(subject, pub, priv);
        PublicKey = pub;
    }

    public bool IsConfigured => _vapid is not null;
    public string? PublicKey { get; }

    public static bool IsAllowedEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 600) return false;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        if (u.HostNameType != UriHostNameType.Dns) return false;
        var host = u.Host.ToLowerInvariant();
        return AllowedHosts.Any(h => host == h || host.EndsWith("." + h));
    }

    public async Task SubscribeAsync(int studentId, string endpoint, string p256dh, string auth, string? userAgent)
    {
        if (!IsAllowedEndpoint(endpoint)) throw new StudentApiException(400, "That browser's push service is not supported.");
        if (string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth) || p256dh.Length > 200 || auth.Length > 100)
            throw new StudentApiException(400, "Invalid push subscription.");

        // The same browser can only belong to one student at a time (a shared computer after a different login).
        await _db.From<PushSubscriptionRow>().Filter("endpoint", Operator.Equals, endpoint).Delete();

        var mine = (await _db.From<PushSubscriptionRow>().Filter("student_id", Operator.Equals, studentId.ToString())
            .Order("created_at", Ordering.Ascending).Get()).Models;
        foreach (var old in mine.Take(Math.Max(0, mine.Count - (MaxSubscriptionsPerStudent - 1))))
            await _db.From<PushSubscriptionRow>().Filter("id", Operator.Equals, old.Id.ToString()).Delete();

        await _db.From<PushSubscriptionRow>().Insert(new PushSubscriptionRow
        {
            StudentId = studentId, Endpoint = endpoint, P256dh = p256dh, Auth = auth,
            UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent
        });
    }

    public async Task UnsubscribeAsync(int studentId, string endpoint) =>
        await _db.From<PushSubscriptionRow>()
            .Filter("student_id", Operator.Equals, studentId.ToString())
            .Filter("endpoint", Operator.Equals, endpoint).Delete();

    /// <summary>Sends to every browser the student enabled. Returns how many were delivered to a push relay. Never throws.</summary>
    public async Task<int> SendAsync(int studentId, string body, int? complaintId, string title = "ITS ResFix")
    {
        if (_vapid is null) return 0;
        try
        {
            var subs = (await _db.From<PushSubscriptionRow>().Filter("student_id", Operator.Equals, studentId.ToString()).Get()).Models;
            if (subs.Count == 0) return 0;
            var payload = JsonSerializer.Serialize(new { title, body = body.Length > 180 ? body[..177] + "..." : body, complaintId });
            var sent = 0;
            foreach (var s in subs)
            {
                if (!IsAllowedEndpoint(s.Endpoint)) continue;
                try
                {
                    await _client.SendNotificationAsync(new PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, _vapid);
                    sent++;
                }
                catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
                {
                    // The browser removed this subscription: forget it.
                    await _db.From<PushSubscriptionRow>().Filter("id", Operator.Equals, s.Id.ToString()).Delete();
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Push to one browser of student {Id} failed", studentId);
                }
            }
            return sent;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Push for student {Id} failed", studentId);
            return 0;
        }
    }

    public async Task<bool> HasSubscriptionAsync(int studentId) =>
        (await _db.From<PushSubscriptionRow>().Filter("student_id", Operator.Equals, studentId.ToString()).Limit(1).Get()).Models.Count > 0;
}
