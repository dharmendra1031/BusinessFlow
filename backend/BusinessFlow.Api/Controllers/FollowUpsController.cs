using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/followups")]
public sealed class FollowUpsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(bool overdueOnly = false, string view = "pending", int page = 1, int pageSize = 50)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue ||
            view is not ("pending" or "overdue" or "completed" or "all"))
            return BadRequest(new { message = "Invalid follow-up filters or pagination." });
        var leadIds = LeadAccess.VisibleTo(db.Leads, User).Select(l => l.Id);
        var query = db.LeadFollowUps.AsNoTracking().Where(f => leadIds.Contains(f.LeadId));
        if (overdueOnly || view == "overdue")
            query = query.Where(f => f.CompletedAtUtc == null && f.DueAtUtc < DateTime.UtcNow);
        else if (view == "pending") query = query.Where(f => f.CompletedAtUtc == null);
        else if (view == "completed") query = query.Where(f => f.CompletedAtUtc != null);
        var total = await query.CountAsync();
        var results = await query.OrderBy(f => f.CompletedAtUtc != null)
            .ThenBy(f => f.DueAtUtc).ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new { f.Id, f.LeadId, LeadName = f.Lead.Name, f.Notes,
                f.DueAtUtc, f.CompletedAtUtc }).ToListAsync();
        return Ok(new { total, page, pageSize, items = results });
    }

    [HttpPost("/api/leads/{leadId:long}/followups")]
    public async Task<IActionResult> Create(long leadId, CreateFollowUpRequest request)
    {
        var lead = await LeadAccess.VisibleTo(db.Leads, User)
            .FirstOrDefaultAsync(l => l.Id == leadId);
        if (lead is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Notes))
            return BadRequest(new { message = "Notes are required." });
        if (request.DueAtUtc <= DateTime.UtcNow)
            return BadRequest(new { message = "Due date must be in the future." });
        var creatorId = LeadAccess.UserId(User);
        var followUp = new LeadFollowUp
        {
            LeadId = leadId, AssignedToId = lead.AssignedToId ?? creatorId,
            CreatedById = creatorId, DueAtUtc = request.DueAtUtc, Notes = request.Notes.Trim()
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.LeadFollowUps.Add(followUp);
        await db.SaveChangesAsync();
        lead.NextFollowUpDate = await db.LeadFollowUps
            .Where(f => f.LeadId == leadId && f.CompletedAtUtc == null)
            .MinAsync(f => (DateTime?)f.DueAtUtc);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = creatorId, Action = "Create", TableName = "FollowUps", RecordId = followUp.Id
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Ok(new { followUp.Id });
    }

    [HttpPatch("{id:long}/complete")]
    public async Task<IActionResult> Complete(long id)
    {
        var leadIds = LeadAccess.VisibleTo(db.Leads, User).Select(l => l.Id);
        var followUp = await db.LeadFollowUps.FirstOrDefaultAsync(f => f.Id == id && leadIds.Contains(f.LeadId));
        if (followUp is null) return NotFound();
        if (followUp.CompletedAtUtc is not null) return NoContent();
        followUp.CompletedAtUtc = DateTime.UtcNow;
        var lead = await db.Leads.FirstAsync(l => l.Id == followUp.LeadId);
        lead.LastFollowUpDate = followUp.CompletedAtUtc;
        lead.NextFollowUpDate = await db.LeadFollowUps
            .Where(f => f.LeadId == lead.Id && f.Id != followUp.Id && f.CompletedAtUtc == null)
            .MinAsync(f => (DateTime?)f.DueAtUtc);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = "Complete",
            TableName = "FollowUps", RecordId = id
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpdateFollowUpRequest request)
    {
        var leadIds = LeadAccess.VisibleTo(db.Leads, User).Select(l => l.Id);
        var followUp = await db.LeadFollowUps
            .FirstOrDefaultAsync(f => f.Id == id && leadIds.Contains(f.LeadId));
        if (followUp is null) return NotFound();
        if (followUp.CompletedAtUtc.HasValue)
            return BadRequest(new { message = "Completed follow-ups cannot be edited." });
        if (request.DueAtUtc <= DateTime.UtcNow || string.IsNullOrWhiteSpace(request.Notes))
            return BadRequest(new { message = "Enter a future due date and remarks." });

        followUp.DueAtUtc = request.DueAtUtc;
        followUp.Notes = request.Notes.Trim();
        var lead = await db.Leads.FirstAsync(l => l.Id == followUp.LeadId);
        var otherDue = await db.LeadFollowUps.AsNoTracking()
            .Where(f => f.LeadId == lead.Id && f.Id != id && f.CompletedAtUtc == null)
            .MinAsync(f => (DateTime?)f.DueAtUtc);
        lead.NextFollowUpDate = otherDue.HasValue && otherDue.Value < followUp.DueAtUtc
            ? otherDue : followUp.DueAtUtc;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = "Update",
            TableName = "FollowUps", RecordId = id, ChangedFields = "Due date, remarks"
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("/api/leads/{leadId:long}/followups")]
    public async Task<IActionResult> History(long leadId)
    {
        if (!await LeadAccess.VisibleTo(db.Leads, User).AnyAsync(l => l.Id == leadId))
            return NotFound();
        var followUps = await db.LeadFollowUps.AsNoTracking()
            .Where(f => f.LeadId == leadId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new { f.Id, f.Notes, f.DueAtUtc, f.CompletedAtUtc, f.CreatedAtUtc, f.AssignedToId })
            .ToListAsync();
        return Ok(followUps);
    }
}
