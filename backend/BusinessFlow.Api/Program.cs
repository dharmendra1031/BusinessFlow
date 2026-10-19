using System.Text;
using System.Threading.RateLimiting;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var secret = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("Configure Jwt:Key with at least 32 UTF-8 bytes.");
if (builder.Environment.IsProduction() && secret.StartsWith("REPLACE_WITH_", StringComparison.Ordinal))
    throw new InvalidOperationException("Replace the production Jwt:Key placeholder before starting the API.");

builder.Services.AddControllers();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "CRM.Antiforgery";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddProblemDetails();
var loginLimit = builder.Configuration.GetValue<int?>("RateLimiting:LoginRequestsPerMinute") ?? 5;
if (loginLimit is < 1 or > 50)
    throw new InvalidOperationException("Login rate limit must be between 1 and 50 per minute.");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Configure DefaultConnection.");
if (builder.Environment.IsProduction() && connectionString.Contains("YOUR_SQL_", StringComparison.Ordinal))
    throw new InvalidOperationException("Replace the production SQL Server connection placeholders before starting the API.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddRoles<IdentityRole<long>>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<RolePermissionService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Request.Headers.Authorization))
                    context.Token = context.Request.Cookies["CRM.Session"];
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirst("sub")?.Value;
                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = userId is null ? null : await userManager.FindByIdAsync(userId);
                if (user is null || !user.IsActive)
                {
                    context.Fail("User disabled or no longer exists.");
                    return;
                }

                if (user.SecurityStamp != context.Principal?.FindFirst("security_stamp")?.Value)
                {
                    context.Fail("Session is no longer valid.");
                    return;
                }

                var currentRoles = await userManager.GetRolesAsync(user);
                var tokenRoles = context.Principal!.FindAll("role").Select(claim => claim.Value);
                if (!currentRoles.OrderBy(role => role).SequenceEqual(tokenRoles.OrderBy(role => role)))
                    context.Fail("User permissions changed. Sign in again.");
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    if (!app.Environment.IsDevelopment())
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
            "form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
            "connect-src 'self'; upgrade-insecure-requests";
    await next();
});

app.UseMiddleware<RequestDiagnosticsMiddleware>();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    if (context.Request.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) &&
        !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method) &&
        !string.Equals(context.Request.Path.Value, "/api/auth/login", StringComparison.OrdinalIgnoreCase) &&
        context.Request.Cookies.ContainsKey("CRM.Session") &&
        !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid security token. Refresh the page and sign in again." });
            return;
        }
    }
    await next();
});
app.Use(async (context, next) =>
{
    var permission = RolePermissionService.ForRequest(context.Request);
    if (permission is not null && context.User.Identity?.IsAuthenticated == true)
    {
        var rules = context.RequestServices.GetRequiredService<RolePermissionService>();
        if (!await rules.AllowedAsync(context.User, permission))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }
    await next();
});
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var index = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
    if (!File.Exists(index))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(index);
});
await DbInitializer.SeedAdminAsync(app.Services, app.Configuration);
app.Run();
