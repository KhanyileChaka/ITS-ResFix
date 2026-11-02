using System.Net;

namespace ITSResFix.Staff.Services;

/// <summary>HTML + plain-text versions of the emails ITS ResFix sends. Every dynamic value is HTML-encoded.</summary>
public static class EmailTemplates
{
    private const string Green = "#16a34a", GreenSoft = "#dcfce7", Ink = "#0f172a", Muted = "#64748b", Line = "#e5eaf0";

    private static string E(string? v) => WebUtility.HtmlEncode(v ?? "");
    private static string Multiline(string? v) => E(v).Replace("\r\n", "\n").Replace("\n", "<br>");

    /// <summary>The email an external company receives when a complaint is escalated to them.</summary>
    public static (string Html, string Text) Escalation(string company, string job, string category, string room,
        string priority, string description, string? photoUrl = null)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(photoUrl) && photoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var emergency = string.Equals(priority, "Emergency", StringComparison.OrdinalIgnoreCase);
        var badge = emergency
            ? "<span style=\"display:inline-block;padding:3px 10px;border-radius:999px;background:#fdecec;color:#b91c1c;font-weight:700;font-size:12px;\">Emergency</span>"
            : "<span style=\"display:inline-block;padding:3px 10px;border-radius:999px;background:#eef1f5;color:#475569;font-weight:700;font-size:12px;\">Normal</span>";

        string Row(string label, string valueHtml) =>
            $"<tr><td style=\"padding:10px 0;border-top:1px solid {Line};color:{Muted};font-size:13px;width:120px;\">{label}</td>" +
            $"<td style=\"padding:10px 0;border-top:1px solid {Line};color:{Ink};font-size:15px;font-weight:600;\">{valueHtml}</td></tr>";

        var html = $"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ITS ResFix maintenance request {E(job)}</title></head>
            <body style="margin:0;padding:0;background:#f4f6f8;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;padding:24px 12px;"><tr><td align="center">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid {Line};">
                <tr><td style="background:{Green};padding:22px 28px;">
                  <table role="presentation" cellpadding="0" cellspacing="0"><tr>
                    <td style="padding-right:14px;"><img src="cid:logo" width="48" height="48" alt="ITS ResFix" style="display:block;border-radius:10px;background:#ffffff;"></td>
                    <td style="color:#ffffff;"><div style="font-size:20px;font-weight:800;letter-spacing:-0.01em;">ITS ResFix</div><div style="font-size:13px;opacity:.9;">Residence maintenance</div></td>
                  </tr></table>
                </td></tr>
                <tr><td style="padding:28px 28px 8px;">
                  <div style="font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:{Green};">Maintenance request escalated</div>
                  <h1 style="margin:6px 0 10px;font-size:24px;line-height:1.25;color:{Ink};">A job needs your team, {E(company)}</h1>
                  <p style="margin:0;font-size:15px;line-height:1.6;color:#334155;">The internal team could not complete this request in time, so we are passing it to you. The details are below.</p>
                </td></tr>
                <tr><td style="padding:16px 28px 4px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-bottom:1px solid {Line};">
                    {Row("Job", $"<span style=\"font-family:Consolas,Menlo,monospace;\">{E(job)}</span>")}
                    {Row("Category", E(category))}
                    {Row("Room", E(room))}
                    {Row("Priority", badge)}
                    {(hasPhoto ? Row("Photo", $"<a href=\"{E(photoUrl!)}\" style=\"color:{Green};\">Open the student's photo</a>") : "")}
                  </table>
                </td></tr>
                <tr><td style="padding:20px 28px 4px;">
                  <div style="font-size:13px;color:{Muted};margin-bottom:6px;">What the student reported</div>
                  <div style="background:#f8fafc;border:1px solid {Line};border-left:4px solid {Green};border-radius:10px;padding:14px 16px;font-size:15px;line-height:1.6;color:{Ink};">{Multiline(description)}</div>
                </td></tr>
                <tr><td style="padding:24px 28px 8px;">
                  <div style="background:{GreenSoft};border-radius:12px;padding:16px 18px;font-size:15px;line-height:1.55;color:#14532d;">
                    <strong>Next step:</strong> please reply to this email to confirm when you can attend.
                  </div>
                </td></tr>
                <tr><td style="padding:20px 28px 28px;font-size:12px;line-height:1.6;color:{Muted};">
                  You are receiving this because {E(company)} is the registered external maintenance contact for ITS ResFix.
                  Please do not forward this email: it contains details about a student's room.
                </td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;

        var text = $"Hello {company},\n\nITS ResFix has escalated a maintenance request to you.\n\n" +
            $"Job:       {job}\nCategory:  {category}\nRoom:      {room}\nPriority:  {priority}\n" + (hasPhoto ? $"Photo:     {photoUrl}\n" : "") + "\n" +
            $"What the student reported:\n{description}\n\nPlease reply to this email to confirm when you can attend.\n";
        return (html, text);
    }

    /// <summary>The email a staff member gets with their password reset link.</summary>
    public static (string Html, string Text) PasswordReset(string name, string link)
    {
        var html = $"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Reset your ITS ResFix password</title></head>
            <body style="margin:0;padding:0;background:#f4f6f8;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;padding:24px 12px;"><tr><td align="center">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid {Line};">
                <tr><td style="background:{Green};padding:22px 28px;">
                  <table role="presentation" cellpadding="0" cellspacing="0"><tr>
                    <td style="padding-right:14px;"><img src="cid:logo" width="48" height="48" alt="ITS ResFix" style="display:block;border-radius:10px;background:#ffffff;"></td>
                    <td style="color:#ffffff;"><div style="font-size:20px;font-weight:800;letter-spacing:-0.01em;">ITS ResFix</div><div style="font-size:13px;opacity:.9;">Staff portal</div></td>
                  </tr></table>
                </td></tr>
                <tr><td style="padding:28px 28px 8px;">
                  <div style="font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:{Green};">Password reset</div>
                  <h1 style="margin:6px 0 10px;font-size:24px;line-height:1.25;color:{Ink};">Reset your password, {E(name)}</h1>
                  <p style="margin:0;font-size:15px;line-height:1.6;color:#334155;">We received a request to reset the password on your ITS ResFix staff account. Choose the button below to set a new one.</p>
                </td></tr>
                <tr><td style="padding:22px 28px 6px;" align="left">
                  <a href="{E(link)}" style="display:inline-block;background:{Green};color:#ffffff;text-decoration:none;font-weight:700;font-size:15px;padding:13px 26px;border-radius:12px;">Choose a new password</a>
                </td></tr>
                <tr><td style="padding:14px 28px 4px;font-size:13px;line-height:1.6;color:{Muted};">
                  This link works once and expires in 30 minutes. If the button does not work, copy this address into your browser:<br>
                  <span style="word-break:break-all;color:#334155;">{E(link)}</span>
                </td></tr>
                <tr><td style="padding:20px 28px 28px;">
                  <div style="background:#fff6e0;border-radius:12px;padding:14px 16px;font-size:13px;line-height:1.55;color:#7a5200;">
                    <strong>Did not ask for this?</strong> You can ignore this email. Your password will not change unless the link is used.
                  </div>
                </td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
        var text = $"Hello {name},\n\nWe received a request to reset the password on your ITS ResFix staff account.\n\n" +
                   $"Choose a new password here (works once, expires in 30 minutes):\n{link}\n\n" +
                   "If you did not ask for this, ignore this email; your password will not change.\n";
        return (html, text);
    }

    /// <summary>The email a new worker gets: where to sign in, which email to use, and a link to choose their password.</summary>
    public static (string Html, string Text) WorkerWelcome(string name, string loginEmail, string signInUrl, string setPasswordLink)
    {
        var html = $"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Welcome to ITS ResFix</title></head>
            <body style="margin:0;padding:0;background:#f4f6f8;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;padding:24px 12px;"><tr><td align="center">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid {Line};">
                <tr><td style="background:{Green};padding:22px 28px;">
                  <table role="presentation" cellpadding="0" cellspacing="0"><tr>
                    <td style="padding-right:14px;"><img src="cid:logo" width="48" height="48" alt="ITS ResFix" style="display:block;border-radius:10px;background:#ffffff;"></td>
                    <td style="color:#ffffff;"><div style="font-size:20px;font-weight:800;letter-spacing:-0.01em;">ITS ResFix</div><div style="font-size:13px;opacity:.9;">Staff portal</div></td>
                  </tr></table>
                </td></tr>
                <tr><td style="padding:28px 28px 8px;">
                  <div style="font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:{Green};">Welcome to the team</div>
                  <h1 style="margin:6px 0 10px;font-size:24px;line-height:1.25;color:{Ink};">Hi {E(name)}, your account is ready</h1>
                  <p style="margin:0;font-size:15px;line-height:1.6;color:#334155;">The residence manager has added you as a maintenance worker on ITS ResFix. You will see the jobs assigned to you, update their progress and message students from the staff portal.</p>
                </td></tr>
                <tr><td style="padding:18px 28px 4px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-bottom:1px solid {Line};">
                    <tr><td style="padding:10px 0;border-top:1px solid {Line};color:{Muted};font-size:13px;width:120px;">Sign in at</td><td style="padding:10px 0;border-top:1px solid {Line};font-size:14px;font-weight:600;word-break:break-all;"><a href="{E(signInUrl)}" style="color:{Green};text-decoration:none;">{E(signInUrl)}</a></td></tr>
                    <tr><td style="padding:10px 0;border-top:1px solid {Line};color:{Muted};font-size:13px;">Your email</td><td style="padding:10px 0;border-top:1px solid {Line};font-size:15px;font-weight:600;color:{Ink};">{E(loginEmail)}</td></tr>
                  </table>
                </td></tr>
                <tr><td style="padding:22px 28px 6px;" align="left">
                  <a href="{E(setPasswordLink)}" style="display:inline-block;background:{Green};color:#ffffff;text-decoration:none;font-weight:700;font-size:15px;padding:13px 26px;border-radius:12px;">Choose your password</a>
                </td></tr>
                <tr><td style="padding:14px 28px 4px;font-size:13px;line-height:1.6;color:{Muted};">
                  This link works once and expires in 48 hours. If the button does not work, copy this address into your browser:<br>
                  <span style="word-break:break-all;color:#334155;">{E(setPasswordLink)}</span>
                </td></tr>
                <tr><td style="padding:20px 28px 28px;">
                  <div style="background:#fff6e0;border-radius:12px;padding:14px 16px;font-size:13px;line-height:1.55;color:#7a5200;">
                    <strong>Keep it private.</strong> Never share your password. If the link has expired, use "Forgot password?" on the sign-in page.
                  </div>
                </td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
        var text = $"Hi {name},\n\nThe residence manager has added you as a maintenance worker on ITS ResFix.\n\n" +
                   $"Sign in at: {signInUrl}\nYour email: {loginEmail}\n\n" +
                   $"Choose your password here (works once, expires in 48 hours):\n{setPasswordLink}\n\n" +
                   "If the link has expired, use \"Forgot password?\" on the sign-in page.\n";
        return (html, text);
    }

    /// <summary>The email a student gets to start: open the app, choose First Login, use their cellphone number.</summary>
    public static (string Html, string Text) StudentInvite(string name, string appUrl, string cellphone)
    {
        string Step(int n, string title, string body) =>
            $"<tr><td style=\"padding:10px 0;vertical-align:top;width:42px;\"><div style=\"width:30px;height:30px;border-radius:50%;background:{GreenSoft};color:{Green};font-weight:800;text-align:center;line-height:30px;\">{n}</div></td>" +
            $"<td style=\"padding:10px 0;\"><div style=\"font-weight:700;font-size:15px;color:{Ink};\">{title}</div><div style=\"font-size:14px;color:#475569;line-height:1.55;\">{body}</div></td></tr>";
        var html = $"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Get started with ITS ResFix</title></head>
            <body style="margin:0;padding:0;background:#f4f6f8;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;padding:24px 12px;"><tr><td align="center">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid {Line};">
                <tr><td style="background:{Green};padding:22px 28px;">
                  <table role="presentation" cellpadding="0" cellspacing="0"><tr>
                    <td style="padding-right:14px;"><img src="cid:logo" width="48" height="48" alt="ITS ResFix" style="display:block;border-radius:10px;background:#ffffff;"></td>
                    <td style="color:#ffffff;"><div style="font-size:20px;font-weight:800;letter-spacing:-0.01em;">ITS ResFix</div><div style="font-size:13px;opacity:.9;">Residence maintenance</div></td>
                  </tr></table>
                </td></tr>
                <tr><td style="padding:28px 28px 8px;">
                  <div style="font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:{Green};">Welcome</div>
                  <h1 style="margin:6px 0 10px;font-size:24px;line-height:1.25;color:{Ink};">Hi {E(name)}, your account is ready</h1>
                  <p style="margin:0;font-size:15px;line-height:1.6;color:#334155;">Report a problem in your room, follow its progress and chat with the maintenance team, all from your phone.</p>
                </td></tr>
                <tr><td style="padding:18px 28px 4px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                    {Step(1, "Open the app", "Tap the button below on your phone. You can also choose Add to home screen in your browser menu to install it.")}
                    {Step(2, "Choose First Login", "On the sign-in screen, pick First Login.")}
                    {Step(3, "Enter your cellphone number", $"Use <strong>{E(cellphone)}</strong>. We will text you a verification code.")}
                    {Step(4, "Create your password", "Type the code, then choose a password you will remember.")}
                  </table>
                </td></tr>
                <tr><td style="padding:14px 28px 6px;" align="left">
                  <a href="{E(appUrl)}" style="display:inline-block;background:{Green};color:#ffffff;text-decoration:none;font-weight:700;font-size:15px;padding:13px 26px;border-radius:12px;">Open ITS ResFix</a>
                </td></tr>
                <tr><td style="padding:14px 28px 28px;font-size:13px;line-height:1.6;color:{Muted};">
                  If the button does not work, copy this address into your browser:<br><span style="word-break:break-all;color:#334155;">{E(appUrl)}</span>
                </td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
        var text = $"Hi {name},\n\nYour ITS ResFix account is ready. Report problems in your room, follow their progress and chat with the maintenance team.\n\n" +
                   $"1. Open the app: {appUrl}\n2. Choose First Login\n3. Enter your cellphone number ({cellphone}); we will text you a verification code\n4. Type the code and choose a password\n";
        return (html, text);
    }

    /// <summary>The shared look of the newer emails: logo band, heading, intro, a block of detail rows, an optional button and a footer.</summary>
    private static string Frame(string title, string eyebrow, string heading, string intro, string rowsHtml, string? buttonText, string? buttonUrl, string footer)
    {
        var button = buttonUrl is null ? "" :
            $"<tr><td style=\"padding:14px 28px 6px;\" align=\"left\"><a href=\"{E(buttonUrl)}\" style=\"display:inline-block;background:{Green};color:#ffffff;text-decoration:none;font-weight:700;font-size:15px;padding:13px 26px;border-radius:12px;\">{E(buttonText)}</a></td></tr>" +
            $"<tr><td style=\"padding:10px 28px 0;font-size:12px;line-height:1.6;color:{Muted};\">If the button does not work, copy this address into your browser:<br><span style=\"word-break:break-all;color:#334155;\">{E(buttonUrl)}</span></td></tr>";
        return $"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(title)}</title></head>
            <body style="margin:0;padding:0;background:#f4f6f8;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;padding:24px 12px;"><tr><td align="center">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid {Line};">
                <tr><td style="background:{Green};padding:22px 28px;">
                  <table role="presentation" cellpadding="0" cellspacing="0"><tr>
                    <td style="padding-right:14px;"><img src="cid:logo" width="48" height="48" alt="ITS ResFix" style="display:block;border-radius:10px;background:#ffffff;"></td>
                    <td style="color:#ffffff;"><div style="font-size:20px;font-weight:800;letter-spacing:-0.01em;">ITS ResFix</div><div style="font-size:13px;opacity:.9;">Residence maintenance</div></td>
                  </tr></table>
                </td></tr>
                <tr><td style="padding:28px 28px 8px;">
                  <div style="font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:{Green};">{E(eyebrow)}</div>
                  <h1 style="margin:6px 0 10px;font-size:24px;line-height:1.25;color:{Ink};">{heading}</h1>
                  <p style="margin:0;font-size:15px;line-height:1.6;color:#334155;">{intro}</p>
                </td></tr>
                {rowsHtml}
                {button}
                <tr><td style="padding:18px 28px 28px;font-size:12px;line-height:1.6;color:{Muted};">{footer}</td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
    }

    private static string DetailRow(string label, string valueHtml) =>
        $"<tr><td style=\"padding:10px 0;border-top:1px solid {Line};color:{Muted};font-size:13px;width:120px;\">{label}</td>" +
        $"<td style=\"padding:10px 0;border-top:1px solid {Line};color:{Ink};font-size:15px;font-weight:600;\">{valueHtml}</td></tr>";

    /// <summary>The code a student gets to reset a forgotten password.</summary>
    public static (string Html, string Text) StudentResetCode(string name, string code)
    {
        var codeBox = $"<tr><td style=\"padding:18px 28px 4px;\"><div style=\"background:{GreenSoft};border-radius:12px;padding:18px;text-align:center;font-family:Consolas,Menlo,monospace;font-size:34px;font-weight:800;letter-spacing:.35em;color:#14532d;\">{E(code)}</div></td></tr>";
        var html = Frame("Reset your ITS ResFix password", "Password reset", $"Hi {E(name)}, here is your code",
            "Type this code in the app to choose a new password. It works once and expires in 5 minutes.", codeBox, null, null,
            "If you did not ask to reset your password, ignore this email: nothing changes unless the code is used. Never share the code with anyone.");
        var text = $"Hi {name},\n\nYour ITS ResFix password reset code is {code}. It expires in 5 minutes. Type it in the app to choose a new password.\n\nIf you did not ask for this, ignore this email. Never share the code.\n";
        return (html, text);
    }

    /// <summary>The email a worker gets when a complaint is assigned to them.</summary>
    public static (string Html, string Text) JobAssigned(string name, string job, string category, string room, string priority, string description, string deadline, string link)
    {
        var emergency = string.Equals(priority, "Emergency", StringComparison.OrdinalIgnoreCase);
        var badge = emergency
            ? "<span style=\"display:inline-block;padding:3px 10px;border-radius:999px;background:#fdecec;color:#b91c1c;font-weight:700;font-size:12px;\">Emergency</span>"
            : "<span style=\"display:inline-block;padding:3px 10px;border-radius:999px;background:#eef1f5;color:#475569;font-weight:700;font-size:12px;\">Normal</span>";
        var rows = "<tr><td style=\"padding:16px 28px 4px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-bottom:1px solid " + Line + ";\">" +
            DetailRow("Job", $"<span style=\"font-family:Consolas,Menlo,monospace;\">{E(job)}</span>") + DetailRow("Category", E(category)) + DetailRow("Room", E(room)) +
            DetailRow("Priority", badge) + DetailRow("Respond by", E(deadline)) + "</table></td></tr>" +
            $"<tr><td style=\"padding:16px 28px 4px;\"><div style=\"font-size:13px;color:{Muted};margin-bottom:6px;\">What the student reported</div>" +
            $"<div style=\"background:#f8fafc;border:1px solid {Line};border-left:4px solid {Green};border-radius:10px;padding:14px 16px;font-size:15px;line-height:1.6;color:{Ink};\">{Multiline(description)}</div></td></tr>";
        var html = Frame($"New job {job} assigned to you", emergency ? "Emergency job assigned" : "New job assigned", $"Hi {E(name)}, a job needs you",
            "This complaint was just assigned to you. The 48-hour response timer has started.", rows, "Open the job", link,
            "You are receiving this because you are an internal worker on ITS ResFix. It contains details about a student&rsquo;s room: please do not forward it.");
        var text = $"Hi {name},\n\nA {(emergency ? "EMERGENCY " : "")}job was assigned to you on ITS ResFix.\n\nJob:       {job}\nCategory:  {category}\nRoom:      {room}\nPriority:  {priority}\nRespond by: {deadline}\n\n" +
                   $"What the student reported:\n{description}\n\nOpen the job: {link}\n";
        return (html, text);
    }
}
