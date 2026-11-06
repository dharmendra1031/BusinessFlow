using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;

namespace Resmoh.Web;

public sealed record ProjectEnquiry(
    string? SubmissionKey, string? Service, string? Name, string? Phone,
    string? Email, string? Company, string? Country, string? Budget,
    string? Timeline, string? Details, string? Website, bool ConsentAccepted);

public sealed record WebsiteLead(
    string EventId, string Name, string Phone, string? Email,
    string? CompanyName, string Requirement, bool ConsentAccepted);

public class Program
{
    private static readonly HashSet<string> Services = new(StringComparer.Ordinal)
    {
        "Website", "Mobile App", "ERP / CRM", "POS", "AI Automation", "Custom Software"
    };

    private static readonly HashSet<string> Timelines = new(StringComparer.Ordinal)
    {
        "Not sure", "Within 1 month", "1-3 months", "3-6 months", "Flexible"
    };

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProblemDetails();
        builder.Services.AddHttpClient("businessflow", client =>
            client.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("project-enquiry", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true,
                        QueueLimit = 0
                    }));
        });

        var app = builder.Build();
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseHttpsRedirection();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; " +
                "img-src 'self' data:; connect-src 'self'; form-action 'self'; " +
                "frame-ancestors 'none'; base-uri 'self'";
            await next();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRateLimiter();

        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapPost("/api/project-requests", async (HttpRequest http, ProjectEnquiry enquiry,
            IConfiguration config, IHttpClientFactory clients, CancellationToken ct) =>
        {
            var publicOrigin = config["PublicSite:Origin"]?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(publicOrigin) ||
                !string.Equals(http.Headers.Origin.ToString(), publicOrigin, StringComparison.Ordinal))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (!http.HasJsonContentType() || !Valid(enquiry))
                return Results.BadRequest(new { message = "Please review your project details and contact information." });

            if (!string.IsNullOrWhiteSpace(enquiry.Website))
                return Results.Accepted(value: new { accepted = true });

            var secret = config["BusinessFlow:SigningSecret"];
            var target = config["BusinessFlow:IntakeUrl"];
            if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32 ||
                !Uri.TryCreate(target, UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttps &&
                 !(app.Environment.IsDevelopment() && endpoint.IsLoopback && endpoint.Scheme == Uri.UriSchemeHttp)))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var requirement = string.Join("\n", new[]
            {
                "Service: " + enquiry.Service,
                "Country: " + (string.IsNullOrWhiteSpace(enquiry.Country) ? "Not provided" : enquiry.Country.Trim()),
                "Budget: " + (string.IsNullOrWhiteSpace(enquiry.Budget) ? "Not sure" : enquiry.Budget.Trim()),
                "Timeline: " + enquiry.Timeline,
                "Requirements: " + enquiry.Details!.Trim()
            });
            if (requirement.Length > 2000)
                return Results.BadRequest(new { message = "Please shorten your requirement." });

            var lead = new WebsiteLead("resmoh-" + enquiry.SubmissionKey,
                enquiry.Name!.Trim(), enquiry.Phone!.Trim(), enquiry.Email?.Trim(),
                enquiry.Company?.Trim(), requirement, true);
            var body = JsonSerializer.SerializeToUtf8Bytes(lead, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
            var message = new byte[prefix.Length + body.Length];
            prefix.CopyTo(message, 0);
            body.CopyTo(message, prefix.Length);
            var signature = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message));

            using var outgoing = new HttpRequestMessage(HttpMethod.Post, endpoint);
            outgoing.Content = new ByteArrayContent(body);
            outgoing.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            outgoing.Headers.TryAddWithoutValidation("X-Website-Timestamp", timestamp);
            outgoing.Headers.TryAddWithoutValidation("X-Website-Signature", "sha256=" + signature);

            try
            {
                using var response = await clients.CreateClient("businessflow").SendAsync(outgoing, ct);
                return response.StatusCode == HttpStatusCode.Accepted
                    ? Results.Accepted(value: new { accepted = true })
                    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("project-enquiry").WithMetadata(new RequestSizeLimitAttribute(8192));

        app.Run();
    }

    private static bool Valid(ProjectEnquiry r)
    {
        if (r.SubmissionKey is null || !Regex.IsMatch(r.SubmissionKey, @"^[0-9]{20,32}$") ||
            r.Name is null || r.Name.Trim().Length is < 2 or > 150 ||
            r.Phone is null || r.Phone.Length > 30 ||
            !Regex.IsMatch(r.Phone, @"^\+?[0-9() .-]{7,30}$") ||
            r.Phone.Count(char.IsDigit) is < 7 or > 15 ||
            !Services.Contains(r.Service ?? "") ||
            r.Details is null || r.Details.Trim().Length is < 20 or > 1600 ||
            !Timelines.Contains(r.Timeline ?? "") ||
            r.Company?.Length > 200 || r.Country?.Length > 80 ||
            r.Budget?.Length > 80 || r.Email?.Length > 256 ||
            (!string.IsNullOrWhiteSpace(r.Email) && !new EmailAddressAttribute().IsValid(r.Email)) ||
            !r.ConsentAccepted)
            return false;
        return true;
    }
}
