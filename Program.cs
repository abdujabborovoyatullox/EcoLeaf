using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web_sayt.Data;
using Web_sayt.Services;

var builder = WebApplication.CreateBuilder(args);

// Render PORT environment variable orqali portni beradi
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ---------- PostgreSQL ----------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(ConnectionStringHelper.Resolve(builder.Configuration)));

// Cookie shifrlash kalitlari bazada saqlanadi (qayta deploy qilinganda sessiyalar saqlanib qoladi)
builder.Services.AddDataProtection()
    .SetApplicationName("EcoLeaf")
    .PersistKeysToDbContext<AppDbContext>();

// ---------- Servislar ----------
builder.Services.AddScoped<SettingsService>();
builder.Services.AddHttpClient<ITelegramService, TelegramService>(c => c.Timeout = TimeSpan.FromSeconds(10));

builder.Services
    .AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .ConfigureApiBehaviorOptions(o =>
    {
        // Validatsiya xatolari frontend kutgan { message: "..." } ko'rinishida qaytadi
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var message = ctx.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "Ma'lumotlar noto'g'ri kiritilgan.";
            return new BadRequestObjectResult(new { message });
        };
    });

// ---------- Autentifikatsiya (cookie) ----------
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "ecoleaf.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Render HTTPS'da Secure bo'ladi
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        // API uchun redirect emas, to'g'ridan-to'g'ri 401/403 qaytarilsin
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

// ---------- Brute-force'dan himoya: login/register uchun IP bo'yicha 10 ta so'rov/daqiqa ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Juda ko'p urinish. Bir daqiqadan so'ng qayta urinib ko'ring." }, ct);
    };
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

// Render proxy orqasida haqiqiy IP va https: Dockerfile'dagi ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
// sozlamasi X-Forwarded-For / X-Forwarded-Proto'ni avtomatik yoqadi.

var app = builder.Build();

// Bazani tayyorlash (jadvallar, boshlang'ich mahsulotlar, admin hisobi)
using (var scope = app.Services.CreateScope())
{
    await DbInitializer.InitializeAsync(scope.ServiceProvider);
}

app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(new { message = "Serverda xatolik yuz berdi. Keyinroq qayta urinib ko'ring." });
}));

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    if (ctx.Request.Path.StartsWithSegments("/admin") || ctx.Request.Path.StartsWithSegments("/api/admin"))
        h["X-Robots-Tag"] = "noindex, nofollow";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // HTML har doim yangi holda yuklansin (deploydan keyin eski versiya qolib ketmasin)
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/admin", () => Results.Redirect("/admin.html"));

app.Run();
