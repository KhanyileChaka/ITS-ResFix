using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ITSResFix.Staff.Services;

/// <summary>Issues the signed tokens the student mobile app uses. Key lives in user-secrets (Student:JwtKey).</summary>
public class StudentTokenService
{
    public const string Issuer = "itsresfix";
    public const string Audience = "itsresfix-student";
    public const string SessionPurpose = "session";
    public const string SetupPurpose = "pwsetup";
    public const string ResetPurpose = "pwreset";

    private readonly SymmetricSecurityKey _key;

    public StudentTokenService(IConfiguration config)
    {
        _key = BuildKey(config);
    }

    public static SymmetricSecurityKey BuildKey(IConfiguration config)
    {
        var raw = config["Student:JwtKey"];
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 32)
            throw new InvalidOperationException("Student:JwtKey is missing or too short. Set it with: dotnet user-secrets set \"Student:JwtKey\" \"<random 48+ chars>\"");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(raw));
    }

    public byte[] HmacKey => _key.Key;

    public string IssueSession(int userId, int studentId) =>
        Write(SessionPurpose, TimeSpan.FromDays(7), userId, studentId, null);

    public string IssuePasswordSetup(int userId, int studentId, string cellphone) =>
        Write(SetupPurpose, TimeSpan.FromMinutes(10), userId, studentId, cellphone);

    public string IssuePasswordReset(int userId, int studentId) =>
        Write(ResetPurpose, TimeSpan.FromMinutes(10), userId, studentId, null);

    private string Write(string purpose, TimeSpan life, int userId, int studentId, string? cellphone)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("studentId", studentId.ToString()),
            new("role", "student"),
            new("purpose", purpose),
            new("jti", Guid.NewGuid().ToString("N"))
        };
        if (cellphone is not null) claims.Add(new Claim("cellphone", cellphone));

        var token = new JwtSecurityToken(Issuer, Audience, claims,
            notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.Add(life),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
