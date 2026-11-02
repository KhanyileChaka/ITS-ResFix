using Microsoft.AspNetCore.DataProtection;
using ITSResFix.Staff.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using ITSResFix.Staff.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });
builder.Services.AddControllersWithViews()
    .AddCookieTempDataProvider(o => o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
// The form-token cookie travels over https only once the site is live.
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);

// ---------- Supabase ----------
// Server-side client using the service_role key. Keep this key in user-secrets / environment
// variables only — never in the Flutter app or in source control.
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseKey = builder.Configuration["Supabase:ServiceRoleKey"];
if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
{
    throw new InvalidOperationException(
        "Supabase is not configured. Run: dotnet user-secrets set \"Supabase:Url\" \"https://<project>.supabase.co\" " +
        "and dotnet user-secrets set \"Supabase:ServiceRoleKey\" \"<service_role key>\"");
}

var supabase = new Supabase.Client(supabaseUrl, supabaseKey, new Supabase.SupabaseOptions
{
    AutoConnectRealtime = false,
    AutoRefreshToken = false,
});
await supabase.InitializeAsync();
builder.Services.AddSingleton(supabase);
builder.Services.AddScoped<ResFixDataService>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddHostedService<ThrottleStore>(); // saves the lockout counters so a restart does not forget them
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<StaffSessionValidator>();
builder.Services.AddSingleton<StudentSessionValidator>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<WorkerNotifier>();
builder.Services.AddHttpClient<EmailService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddSingleton<StudentTokenService>();
builder.Services.AddScoped<StudentApiService>();
builder.Services.AddHttpClient<AiTriageService>();
builder.Services.AddHttpClient<CloudinaryService>();
builder.Services.AddHttpClient<SmsService>();
builder.Services.AddSingleton<PushService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<ComplaintReviewWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ComplaintReviewWorker>());

// Sign-in cookies and form tokens are encrypted with keys, and they must survive restarts or everyone is signed out each time.
// DataProtection:KeysPath = a persistent folder (a paid host's disk). Otherwise, outside development, the keys live in the
// database (table data_protection_key), which works on hosting with no disk, like Render's free plan.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("ITSResFix");
else if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddDataProtection().SetApplicationName("ITSResFix");
    builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
        .Configure(o => o.XmlRepository = new SupabaseXmlRepository(supabase));
}

// ---------- Cookie authentication (shared login for managers and workers) ----------
builder.Services
    .AddAuthentication("Smart")
    // Staff pages use the login cookie; /api/student uses the bearer token. Choosing by path means the token is
    // validated early, so the rate limiter can see WHICH student is calling and limit per account.
    .AddPolicyScheme("Smart", "Cookie or token", o => o.ForwardDefaultSelector = ctx =>
        ctx.Request.Path.StartsWithSegments("/api/student") ? "StudentJwt" : CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "ITSResFix.Staff";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var validator = ctx.HttpContext.RequestServices.GetRequiredService<StaffSessionValidator>();
            var data = ctx.HttpContext.RequestServices.GetRequiredService<ResFixDataService>();
            bool ok;
            try { ok = await validator.IsValidAsync(ctx.Principal!, data); }
            catch { ok = true; } // database hiccup: keep the session rather than sign everyone out
            if (!ok)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    })
    // Bearer tokens for the student mobile app (API only; the cookie scheme stays the default for the staff site).
    .AddJwtBearer("StudentJwt", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = StudentTokenService.Issuer,
            ValidateAudience = true, ValidAudience = StudentTokenService.Audience,
            ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
            ValidateIssuerSigningKey = true, IssuerSigningKey = StudentTokenService.BuildKey(builder.Configuration),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            NameClaimType = "sub", RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            // a cancelled account (or one that was set up by the wrong person) stops working straight away
            OnTokenValidated = async ctx =>
            {
                var validator = ctx.HttpContext.RequestServices.GetRequiredService<StudentSessionValidator>();
                if (ctx.Principal is null || !await validator.IsValidAsync(ctx.Principal)) ctx.Fail("Session ended.");
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("StudentSession", p => p
        .AddAuthenticationSchemes("StudentJwt").RequireAuthenticatedUser()
        .RequireClaim("role", "student").RequireClaim("purpose", StudentTokenService.SessionPurpose));
    options.AddPolicy("StudentPasswordReset", p => p
        .AddAuthenticationSchemes("StudentJwt").RequireAuthenticatedUser()
        .RequireClaim("role", "student").RequireClaim("purpose", StudentTokenService.ResetPurpose));
    options.AddPolicy("StudentPasswordSetup", p => p
        .AddAuthenticationSchemes("StudentJwt").RequireAuthenticatedUser()
        .RequireClaim("role", "student").RequireClaim("purpose", StudentTokenService.SetupPurpose));
});

// ---------- CORS (student app running in a browser) ----------
var allowedOrigins = (builder.Configuration["Cors:Origins"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddPolicy("student-app", p => p
    .SetIsOriginAllowed(origin =>
    {
        if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
        return builder.Environment.IsDevelopment() && Uri.TryCreate(origin, UriKind.Absolute, out var u) && (u.Host == "localhost" || u.Host == "127.0.0.1");
    })
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST")));

// ---------- Rate limiting ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync("{\"error\":\"Too many requests. Please wait a moment and try again.\"}", ct);
    };

    // Limits are PER ACCOUNT, never per IP: many students share one residence Wi-Fi/NAT address, so an IP limit would
    // lock out innocent neighbours. Only anonymous calls (no account yet) fall back to a high per-IP flood ceiling.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        if (!ctx.Request.Path.StartsWithSegments("/api/student")) return RateLimitPartition.GetNoLimiter("site");
        var student = ctx.User.FindFirst("studentId")?.Value;
        return student is not null
            ? RateLimitPartition.GetSlidingWindowLimiter($"acct:{student}", _ => new SlidingWindowRateLimiterOptions
                { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 })
            : RateLimitPartition.GetFixedWindowLimiter($"anon-ip:{ctx.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) });
    });

    // Complaint submissions: per student account, never shared.
    o.AddPolicy("submit", ctx => RateLimitPartition.GetFixedWindowLimiter(
        "acct:" + (ctx.User.FindFirst("studentId")?.Value ?? "none"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1) }));
});

// Behind Render's (or any) reverse proxy the real client address and https scheme arrive in X-Forwarded-* headers.
// Without this, every student would look like one address and share a single rate limit.
if (builder.Configuration.GetValue<bool>("Hosting:BehindProxy"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        // only "was this https?" is taken from the proxy; the visitor address comes from CF-Connecting-IP (below), never from a header a visitor can fake
        o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Hosting:BehindProxy"))
{
    app.UseForwardedHeaders();
    // Render sits behind Cloudflare, which overwrites CF-Connecting-IP with the real visitor address (a visitor cannot forge it,
    // unlike X-Forwarded-For). The sign-in and code limits are per address, so they must use this one.
    app.Use(async (ctx, next) =>
    {
        if (System.Net.IPAddress.TryParse(ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault(), out var visitor))
            ctx.Connection.RemoteIpAddress = visitor;
        await next();
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Account/Error");
    app.UseHsts();
}

// The Render address (itsresfix-web.onrender.com) only forwards to the real domain. The API and the health check keep answering on it,
// so copies of the student app that were opened before the move, and Render's own checks, keep working.
var canonicalSite = builder.Configuration["Hosting:PublicUrl"]?.TrimEnd('/');
if (!string.IsNullOrWhiteSpace(canonicalSite))
{
    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        if (ctx.Request.Host.Host.EndsWith(".onrender.com", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/api") && !path.StartsWithSegments("/healthz"))
        {
            ctx.Response.StatusCode = HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method) ? 301 : 308;
            ctx.Response.Headers.Location = canonicalSite + path.ToUriComponent() + ctx.Request.QueryString.ToUriComponent();
            return;
        }
        await next();
    });
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (!ctx.Request.Path.StartsWithSegments("/api"))
        h["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' https://cdn.jsdelivr.net; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
            "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com data:; " +
            "img-src 'self' data: https:; connect-src 'self'; " +
            "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});

// The student app calls the API over plain http on localhost in development (the dev certificate is not trusted).
app.UseWhen(ctx => !(app.Environment.IsDevelopment() && ctx.Request.Path.StartsWithSegments("/api")) && !ctx.Request.Path.StartsWithSegments("/healthz"),
    branch => branch.UseHttpsRedirection());
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// A worker still on a one-time password can only reach the profile page (to replace it) and log out.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.HasClaim(ResFixClaims.MustChange, "1"))
    {
        var path = ctx.Request.Path;
        var allowed = path.StartsWithSegments("/Worker/Profile", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Worker/ChangePassword", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/healthz");
        if (!allowed)
        {
            ctx.Response.Redirect("/Worker/Profile");
            return;
        }
    }
    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();

app.Run();
