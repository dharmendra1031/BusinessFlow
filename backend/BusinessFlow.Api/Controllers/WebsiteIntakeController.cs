using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

public sealed record WebsiteLeadRequest(
    string? EventId, string? Name, string? Phone, string? Email,
    string? CompanyName, string? Requirement, bool ConsentAccepted);

/// <summary>Only website backends may call this route. Never put the signing key in browser JavaScript.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("website-intake")]
[Route("api/integrations/website/leads")]
public sealed class WebsiteIntakeController(AppDbContext db, IConfiguration config, TimeProvider clock) : ControllerBase
{
    private static readonly Regex EventPattern = new("^[A-Za-z0-9_-]{8,100}$", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(
        @"^(?=(?:\D*\d){7,15}\D*$)\+?[0-9() .-]{7,30}$", RegexOptions.Compiled);
    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // Closed by default. Keys and owner IDs belong in environment/secure deployment settings.
        var secret = config["WebsiteIntake:SigningSecret"];
        if (!config.GetValue<bool>("WebsiteIntake:Enabled") ||
            string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32 ||
            !long.TryParse(config["WebsiteIntake:OwnerUserId"], out var ownerId) || ownerId <= 0)
            return StatusCode(503, new { message = "Website intake is not configured." });

        if (!long.TryParse(Request.Headers["X-Website-Timestamp"], out var timestamp))
            return Unauthorized();
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (timestamp < now - 300 || timestamp > now + 300) return Unauthorized();

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();
        if (body.Length is 0 or > 16384) return BadRequest();

        var signature = Request.Headers["X-Website-Signature"].ToString();
        if (!signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase) ||
            signature.Length != 71) return Unauthorized();
        var supplied = new byte[32];
        if (!Convert.TryFromHexString(signature.AsSpan(7), supplied, out var written) || written != 32)
            return Unauthorized();

        var prefix = Encoding.UTF8.GetBytes(timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed, prefix.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) return Unauthorized();

        WebsiteLeadRequest? request;
        try { request = JsonSerializer.Deserialize<WebsiteLeadRequest>(body, JsonOptions); }
        catch (JsonException) { return BadRequest(new { message = "Invalid JSON payload." }); }

        if (request is null || !EventPattern.IsMatch(request.EventId ?? "") ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 2 or > 150 ||
            !PhonePattern.IsMatch(request.Phone ?? "") ||
            request.Email?.Length > 256 ||
            (!string.IsNullOrWhiteSpace(request.Email) && !EmailValidator.IsValid(request.Email)) ||
            request.CompanyName?.Length > 200 || request.Requirement?.Length > 2000 ||
            !request.ConsentAccepted)
            return BadRequest(new { message = "Invalid lead information or missing contact consent." });

        var submissionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.EventId!)));
        var payloadHash = Convert.ToHexString(SHA256.HashData(body));
        var existing = await db.WebsiteLeadSubmissions.AsNoTracking()
            .Where(x => x.SubmissionHash == submissionHash).Select(x => x.PayloadHash).FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing == payloadHash ? Accepted(new { accepted = true })
                : Conflict(new { message = "Submission ID was used with different content." });

        if (!await db.Users.AnyAsync(x => x.Id == ownerId && x.IsActive, ct) ||
            !await db.LeadSources.AnyAsync(x => x.Id == 2 && x.IsActive, ct))
            return StatusCode(503, new { message = "Website intake owner or source is unavailable." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lead = new Lead
        {
            Name = request.Name!.Trim(),
            Phone = request.Phone!.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            CompanyName = request.CompanyName?.Trim(),
            Requirement = request.Requirement?.Trim(),
            LeadGivenBy = "Website",
            SourceId = 2,
            StatusId = 1,
            AssignedToId = ownerId,
            CreatedById = ownerId
        };
        db.Leads.Add(lead);
        try
        {
            await db.SaveChangesAsync(ct);
            lead.CustomerCode = $"CUS-{lead.CreatedAtUtc.Year}-{lead.Id:D6}";
            db.WebsiteLeadSubmissions.Add(new WebsiteLeadSubmission
            {
                LeadId = lead.Id, SubmissionHash = submissionHash, PayloadHash = payloadHash
            });
            db.AuditLogs.Add(new AuditLog
            {
                UserId = ownerId, Action = "Create", TableName = "Leads",
                RecordId = lead.Id, ChangedFields = "Website signed intake"
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (IsDuplicateSubmission(e))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var prior = await db.WebsiteLeadSubmissions.AsNoTracking()
                .Where(x => x.SubmissionHash == submissionHash).Select(x => x.PayloadHash).FirstOrDefaultAsync(ct);
            return prior == payloadHash ? Accepted(new { accepted = true })
                : Conflict(new { message = "Submission ID was used with different content." });
        }

        return Accepted(new { accepted = true });
    }

    private static bool IsDuplicateSubmission(Exception exception)
    {
        for (Exception? error = exception; error is not null; error = error.InnerException)
            if (error is SqlException { Number: 2601 or 2627 }) return true;
        return false;
    }
}
