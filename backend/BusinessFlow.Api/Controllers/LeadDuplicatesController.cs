using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

public sealed record ReviewDuplicateRequest(string? Decision, string? Comment);

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/lead-duplicates")]
public sealed class LeadDuplicatesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string state = "Pending", int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        if (state is not ("Pending" or "Reviewed") || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { message = "Invalid review filter or pagination." });

        var phones = from left in db.Leads.AsNoTracking()
            join right in db.Leads.AsNoTracking() on left.NormalizedPhone equals right.NormalizedPhone
            where left.Id < right.Id && left.NormalizedPhone.Length >= 7
            select new { LeftId = left.Id, RightId = right.Id };
        var emails = from left in db.Leads.AsNoTracking()
            join right in db.Leads.AsNoTracking() on left.NormalizedEmail equals right.NormalizedEmail
            where left.Id < right.Id && left.NormalizedEmail != null && left.NormalizedEmail != ""
            select new { LeftId = left.Id, RightId = right.Id };
        var pairs = phones.Union(emails);

        var matches = from pair in pairs
            join first in db.Leads.AsNoTracking() on pair.LeftId equals first.Id
            join second in db.Leads.AsNoTracking() on pair.RightId equals second.Id
            join reviewed in db.LeadDuplicateReviews.AsNoTracking()
                on new { pair.LeftId, pair.RightId } equals new { LeftId = reviewed.LeftLeadId, RightId = reviewed.RightLeadId }
                into decisions
            from decision in decisions.DefaultIfEmpty()
            where state == "Pending" ? decision == null : decision != null
            select new
            {
                firstId = first.Id, firstName = first.Name, firstPhone = first.Phone,
                firstEmail = first.Email, firstCustomerCode = first.CustomerCode,
                secondId = second.Id, secondName = second.Name, secondPhone = second.Phone,
                secondEmail = second.Email, secondCustomerCode = second.CustomerCode,
                matchPhone = first.NormalizedPhone.Length >= 7 && first.NormalizedPhone == second.NormalizedPhone,
                matchEmail = first.NormalizedEmail != null && first.NormalizedEmail == second.NormalizedEmail,
                decision = decision == null ? "Pending" : decision.Decision,
                comment = decision == null ? null : decision.Comment,
                reviewedAtUtc = decision == null ? (DateTime?)null : decision.ReviewedAtUtc
            };

        var total = await matches.CountAsync(ct);
        var items = await matches.OrderBy(x => x.firstId).ThenBy(x => x.secondId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("{firstId:long}/{secondId:long}")]
    public async Task<IActionResult> Review(long firstId, long secondId, ReviewDuplicateRequest request,
        CancellationToken ct)
    {
        if (firstId <= 0 || firstId >= secondId || request.Decision is not ("Confirmed" or "NotDuplicate"))
            return BadRequest(new { message = "Invalid pair or decision." });
        var comment = request.Comment?.Trim() ?? "";
        if (comment.Length is < 5 or > 500)
            return BadRequest(new { message = "A review note of 5 to 500 characters is required." });

        var pair = from first in db.Leads.AsNoTracking()
            join second in db.Leads.AsNoTracking() on first.Id equals firstId
            where second.Id == secondId &&
                ((first.NormalizedPhone.Length >= 7 && first.NormalizedPhone == second.NormalizedPhone) ||
                  (first.NormalizedEmail != null && first.NormalizedEmail == second.NormalizedEmail))
            select first.Id;
        if (!await pair.AnyAsync(ct)) return NotFound();
        if (await db.LeadDuplicateReviews.AnyAsync(x => x.LeftLeadId == firstId && x.RightLeadId == secondId, ct))
            return Conflict(new { message = "This pair has already been reviewed." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.LeadDuplicateReviews.Add(new LeadDuplicateReview
        {
            LeftLeadId = firstId, RightLeadId = secondId,
            Decision = request.Decision, Comment = comment,
            ReviewedById = LeadAccess.UserId(User)
        });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = "Review",
            TableName = "LeadDuplicateReviews", RecordId = firstId,
            ChangedFields = $"Pair {firstId}/{secondId}: {request.Decision}; {comment}"
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (IsUniqueConflict(e))
        {
            return Conflict(new { message = "This pair has already been reviewed." });
        }
        return NoContent();
    }

    private static bool IsUniqueConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is SqlException { Number: 2601 or 2627 }) return true;
        return false;
    }
}
