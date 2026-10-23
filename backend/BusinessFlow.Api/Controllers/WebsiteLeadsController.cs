using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

public sealed record WebsiteLeadRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [Required, StringLength(30), RegularExpression(@"^(?=(?:\D*\d){7,15}\D*$)\+?[0-9() .-]{7,30}$")] string Phone,
    [EmailAddress, MaxLength(256)] string? Email,
    [MaxLength(200)] string? CompanyName,
    [MaxLength(2000)] string? Requirement);

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("website-intake")]
[Route("api/integrations/website/leads")]
public sealed class WebsiteLeadsController(AppDbContext db, IConfiguration config) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Capture(WebsiteLeadRequest request,
        [FromHeader(Name = "X-Website-Key")] string? apiKey,
        [FromHeader(Name = "X-Submission-Id")] string? submissionId,
        CancellationToken ct)
    {
        var configuredKey = config["CRM_WEBSITE_INGEST_KEY"];
        if (string.IsNullOrEmpty(configuredKey) || Encoding.UTF8.GetByteCount(configuredKey) < 32 ||
            !long.TryParse(config["CRM_WEBSITE_OWNER_ID"], out var ownerId) || ownerId <= 0)
            return StatusCode(503, new { message = "Website intake is not configured." });
        if (apiKey is null || apiKey.Length > 256 || !FixedTimeMatch(apiKey, configuredKey))
            return Unauthorized();
        if (submissionId is null || submissionId.Length is < 8 or > 128 ||
            !submissionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return BadRequest(new { message = "A valid unique X-Submission-Id is required." });

        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == ownerId && x.IsActive, ct))
            return StatusCode(503, new { message = "Website intake owner is unavailable." });
        if (!await db.LeadSources.AsNoTracking().AnyAsync(x => x.Id == 2 && x.IsActive, ct))
            return StatusCode(503, new { message = "Website source is inactive." });

        var name = request.Name.Trim();
        var phone = request.Phone.Trim();
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var company = string.IsNullOrWhiteSpace(request.CompanyName) ? null : request.CompanyName.Trim();
        var requirement = string.IsNullOrWhiteSpace(request.Requirement) ? null : request.Requirement.Trim();
        if (name.Length < 2) return BadRequest(new { message = "Name is too short." });

        var keyHash = Hash("website:v1:" + submissionId);
        var payloadHash = Hash(JsonSerializer.Serialize(new { name, phone, email, company, requirement }));
        var previous = await db.WebsiteLeadSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubmissionHash == keyHash, ct);
        if (previous is not null) return Existing(previous, payloadHash);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lead = new Lead
        {
            Name = name, Phone = phone, Email = email, CompanyName = company,
            Requirement = requirement, SourceId = 2, StatusId = 1,
            CreatedById = ownerId, AssignedToId = ownerId, LeadGivenBy = "Website"
        };
        db.Leads.Add(lead);
        try
        {
            await db.SaveChangesAsync(ct);
            lead.CustomerCode = $"CUS-{lead.CreatedAtUtc.Year}-{lead.Id:D6}";
            db.WebsiteLeadSubmissions.Add(new WebsiteLeadSubmission
            {
                LeadId = lead.Id, SubmissionHash = keyHash, PayloadHash = payloadHash
            });
            db.AuditLogs.Add(new AuditLog
            {
                UserId = ownerId, Action = "Create", TableName = "Leads", RecordId = lead.Id,
                ChangedFields = "Website intake"
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return StatusCode(201, new { id = lead.Id });
        }
        catch (DbUpdateException e) when (UniqueConflict(e))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var raced = await db.WebsiteLeadSubmissions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SubmissionHash == keyHash, ct);
            return raced is null ? Conflict(new { message = "Submission already exists." }) :
                Existing(raced, payloadHash);
        }
    }

    private IActionResult Existing(WebsiteLeadSubmission previous, string payloadHash) =>
        previous.PayloadHash == payloadHash
            ? Ok(new { id = previous.LeadId, alreadyProcessed = true })
            : Conflict(new { message = "Submission ID was already used with different content." });

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeMatch(string provided, string expected)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static bool UniqueConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is SqlException { Number: 2601 or 2627 }) return true;
        return false;
    }
}
