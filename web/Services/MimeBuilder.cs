using System.Text;

namespace ITSResFix.Staff.Services;

/// <summary>
/// Builds a plain email message (RFC 5322 / MIME) as text: a plain version, an optional HTML version and an optional embedded
/// logo (referenced from the HTML as cid:logo). Used for the Gmail API, which takes a complete message.
/// </summary>
public static class MimeBuilder
{
    public static string Build(string fromName, string fromAddress, string to, string subject, string text, string? html, byte[]? logoJpeg, string? replyTo = null)
    {
        var sb = new StringBuilder();
        void Header(string name, string value) => sb.Append(name).Append(": ").Append(value).Append("\r\n");

        Header("From", $"{EncodeWord(fromName)} <{Clean(fromAddress)}>");
        Header("To", Clean(to));
        if (!string.IsNullOrWhiteSpace(replyTo)) Header("Reply-To", Clean(replyTo));
        Header("Subject", EncodeWord(subject));
        Header("Date", DateTime.UtcNow.ToString("r"));
        Header("MIME-Version", "1.0");

        if (html is null)
        {
            Header("Content-Type", "text/plain; charset=UTF-8");
            Header("Content-Transfer-Encoding", "base64");
            sb.Append("\r\n").Append(B64(text));
            return sb.ToString();
        }

        var alt = "alt_" + Guid.NewGuid().ToString("N");
        Header("Content-Type", $"multipart/alternative; boundary=\"{alt}\"");
        sb.Append("\r\n");

        // plain text, for mail apps that do not show HTML
        sb.Append("--").Append(alt).Append("\r\n");
        sb.Append("Content-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(B64(text)).Append("\r\n");

        if (logoJpeg is null)
        {
            sb.Append("--").Append(alt).Append("\r\n");
            sb.Append("Content-Type: text/html; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(B64(html)).Append("\r\n");
        }
        else
        {
            // the HTML and its embedded logo travel together
            var rel = "rel_" + Guid.NewGuid().ToString("N");
            sb.Append("--").Append(alt).Append("\r\n");
            sb.Append($"Content-Type: multipart/related; boundary=\"{rel}\"\r\n\r\n");
            sb.Append("--").Append(rel).Append("\r\n");
            sb.Append("Content-Type: text/html; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(B64(html)).Append("\r\n");
            sb.Append("--").Append(rel).Append("\r\n");
            sb.Append("Content-Type: image/jpeg; name=\"logo.jpg\"\r\nContent-Transfer-Encoding: base64\r\n");
            sb.Append("Content-ID: <logo>\r\nContent-Disposition: inline; filename=\"logo.jpg\"\r\n\r\n");
            sb.Append(Convert.ToBase64String(logoJpeg, Base64FormattingOptions.InsertLineBreaks)).Append("\r\n");
            sb.Append("--").Append(rel).Append("--\r\n");
        }
        sb.Append("--").Append(alt).Append("--\r\n");
        return sb.ToString();
    }

    /// <summary>Gmail wants the whole message base64url-encoded.</summary>
    public static string ToBase64Url(string message) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(message)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s), Base64FormattingOptions.InsertLineBreaks);

    /// <summary>No line breaks inside a header value, so nothing can inject extra headers.</summary>
    private static string Clean(string s) => s.Replace("\r", "").Replace("\n", "").Trim();

    /// <summary>Non-ASCII header text (names, subjects) is wrapped as an RFC 2047 encoded word.</summary>
    private static string EncodeWord(string s)
    {
        s = Clean(s);
        return s.All(c => c < 128) ? s.Replace("\"", "'") : "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "?=";
    }
}
