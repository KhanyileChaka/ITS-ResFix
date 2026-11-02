using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ITSResFix.Staff.Services;

public record UploadedImage(string Url, string PublicId);

/// <summary>
/// Uploads complaint photos to Cloudinary using signed server-side requests.
/// The API secret stays on the server (user-secrets); the mobile app never sees it and never talks to Cloudinary.
/// Config (user-secrets): Cloudinary:CloudName, Cloudinary:ApiKey, Cloudinary:ApiSecret
///   or the single value Cloudinary:Url = cloudinary://API_KEY:API_SECRET@CLOUD_NAME
/// </summary>
public class CloudinaryService
{
    private static readonly Regex SafeCloud = new("^[a-zA-Z0-9_-]{2,64}$", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<CloudinaryService> _log;
    private readonly string? _cloud;
    private readonly string? _key;
    private readonly string? _secret;
    private readonly string _folder;

    public CloudinaryService(HttpClient http, IConfiguration config, ILogger<CloudinaryService> log)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(40);
        _log = log;
        _folder = config["Cloudinary:Folder"] ?? "itsresfix/complaints";

        var url = config["Cloudinary:Url"];
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "cloudinary")
        {
            var parts = u.UserInfo.Split(':', 2);
            if (parts.Length == 2) { _key = Uri.UnescapeDataString(parts[0]); _secret = Uri.UnescapeDataString(parts[1]); }
            _cloud = u.Host;
        }
        _cloud ??= config["Cloudinary:CloudName"];
        _key ??= config["Cloudinary:ApiKey"];
        _secret ??= config["Cloudinary:ApiSecret"];

        if (IsConfigured && !SafeCloud.IsMatch(_cloud!))
        {
            _log.LogError("Cloudinary cloud name has an unexpected format; photo uploads disabled.");
            _cloud = null;
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_cloud) && !string.IsNullOrWhiteSpace(_key) && !string.IsNullOrWhiteSpace(_secret);

    /// <summary>Upload an already-validated image. The public id is random so links cannot be guessed.</summary>
    public async Task<UploadedImage> UploadAsync(byte[] image, string extension, int studentId, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Cloudinary is not configured.");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var publicId = Guid.NewGuid().ToString("N");
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["allowed_formats"] = "jpg,png,webp",
            ["folder"] = $"{_folder}/{studentId}",
            ["public_id"] = publicId,
            // Re-encode and cap size: strips EXIF/GPS metadata and keeps storage small.
            ["transformation"] = "c_limit,w_1600,h_1600,q_auto",
            ["timestamp"] = timestamp
        };

        using var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        form.Add(new StringContent(_key!), "api_key");
        form.Add(new StringContent(Sign(fields)), "signature");
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(extension == "jpg" ? "image/jpeg" : $"image/{extension}");
        form.Add(file, "file", $"{publicId}.{extension}");

        using var resp = await _http.PostAsync($"https://api.cloudinary.com/v1_1/{_cloud}/image/upload", form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogError("Cloudinary upload failed ({Status}): {Body}", (int)resp.StatusCode, body);
            throw new InvalidOperationException("Cloudinary upload failed.");
        }

        using var json = JsonDocument.Parse(body);
        var secureUrl = json.RootElement.GetProperty("secure_url").GetString();
        var id = json.RootElement.GetProperty("public_id").GetString();
        if (string.IsNullOrEmpty(secureUrl) || !secureUrl.StartsWith("https://res.cloudinary.com/", StringComparison.Ordinal) || id is null)
            throw new InvalidOperationException("Cloudinary returned an unexpected response.");
        return new UploadedImage(secureUrl, id);
    }

    /// <summary>Delete an image given the delivery URL we stored. Ignores URLs that are not from our own cloud.</summary>
    public async Task DeleteByUrlAsync(string? url, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(url)) return;
        var m = Regex.Match(url, $@"^https://res\.cloudinary\.com/{Regex.Escape(_cloud!)}/image/upload/(?:v\d+/)?(.+)\.\w+$");
        if (m.Success) await DeleteAsync(m.Groups[1].Value, ct);
    }

    /// <summary>Remove an image (used when a complaint is deleted or an upload must be rolled back).</summary>
    public async Task DeleteAsync(string publicId, CancellationToken ct = default)
    {
        if (!IsConfigured) return;
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["invalidate"] = "true",
            ["public_id"] = publicId,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
        };
        using var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        form.Add(new StringContent(_key!), "api_key");
        form.Add(new StringContent(Sign(fields)), "signature");
        using var resp = await _http.PostAsync($"https://api.cloudinary.com/v1_1/{_cloud}/image/destroy", form, ct);
        if (!resp.IsSuccessStatusCode) _log.LogWarning("Cloudinary delete failed ({Status}) for {Id}", (int)resp.StatusCode, publicId);
    }

    // Cloudinary signature: SHA-1 of "k1=v1&k2=v2" (sorted, excluding file/api_key/resource_type) + api_secret.
    private string Sign(SortedDictionary<string, string> fields)
    {
        var toSign = string.Join("&", fields.Select(kv => $"{kv.Key}={kv.Value}")) + _secret;
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(toSign))).ToLowerInvariant();
    }
}
