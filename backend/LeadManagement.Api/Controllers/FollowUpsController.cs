using LeadManagement.Api.Contracts;
using LeadManagement.Api.Data;
using LeadManagement.Api.Models;
using LeadManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/followups")]
public sealed class FollowUpsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(bool overdueOnly = false)
    {
        var leadIds = LeadAccess.VisibleTo(db.Leads, User).Select(l => l.Id);
        var query = db.LeadFollowUps.AsNoTracking().Where(f => leadIds.Contains(f.LeadId));
        if (overdueOnly)
            query = query.Where(f => f.CompletedAtUtc == null && f.DueAtUtc < DateTime.UtcNow);
        var results = await query.OrderBy(f => f.CompletedAtUtc != null)
            .ThenBy(f => f.DueAtUtc).Take(200)
            .Select(f => new { f.Id, f.LeadId, LeadName = f.Lead.Name, f.Notes,
                f.DueAtUtc, f.CompletedAtUtc }).ToListAsync();
        return Ok(results);
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
        db.LeadFollowUps.Add(followUp);
        await db.SaveChangesAsync();
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
        await db.SaveChangesAsync();
        return NoContent();
    }
}
