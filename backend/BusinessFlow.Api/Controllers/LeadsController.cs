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
[Route("api/leads")]
public sealed class LeadsController(AppDbContext db, RecordAccessService records) : ControllerBase
{
    private string Action => RolePermissionService.ForRequest(Request) ?? "leads.view";
    [HttpGet]
    [RequirePermission("leads.view")]
    public async Task<IActionResult> List(string? search, long? statusId, int page = 1, int pageSize = 20,
        long? userId = null, string? leadGivenBy = null, DateTime? from = null, DateTime? to = null)
    {
        if (page < 1 || pageSize is < 1 or > 100) return BadRequest();
        if (search?.Length > 150 || leadGivenBy?.Length > 150)
            return BadRequest(new { message = "Search filters must not exceed 150 characters." });
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return BadRequest(new { message = "Invalid date range." });
        var query = (await records.LeadsAsync(User, Action)).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l => l.Name.Contains(term) || l.Phone.Contains(term) || l.CustomerCode!.Contains(term) ||
                                     (l.CompanyName != null && l.CompanyName.Contains(term)));
        }
        if (statusId.HasValue) query = query.Where(l => l.StatusId == statusId.Value);
        if (userId.HasValue) query = query.Where(l => l.AssignedToId == userId.Value);
        if (!string.IsNullOrWhiteSpace(leadGivenBy)) query = query.Where(l => l.LeadGivenBy != null && l.LeadGivenBy.Contains(leadGivenBy));
        if (from.HasValue) query = query.Where(l => l.CreatedAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(l => l.CreatedAtUtc < to.Value.Date.AddDays(1));
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                l.Id, l.RowVersion, l.CustomerCode, l.Name, l.Phone, l.Email, l.CompanyName, l.Address,
                l.LeadGivenBy, l.ProjectAmount, l.LastFollowUpDate, l.NextFollowUpDate, l.Requirement,
                l.SourceId, Source = l.Source.Name, l.StatusId, Status = l.Status.Name,
                l.AssignedToId, AssignedTo = l.AssignedTo == null ? null : l.AssignedTo.FullName,
                l.CreatedAtUtc
            }).ToListAsync();
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("{id:long}")]
    [RequirePermission("leads.view")]
    public async Task<IActionResult> Get(long id)
    {
        var lead = await (await records.LeadsAsync(User, Action)).AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new { l.Id, l.RowVersion, l.CustomerCode, l.Name, l.Phone, l.Email, l.CompanyName, l.Address,
                l.LeadGivenBy, l.ProjectAmount, l.LastFollowUpDate, l.NextFollowUpDate, l.Requirement,
                l.SourceId, l.StatusId, l.AssignedToId, l.CreatedAtUtc, l.UpdatedAtUtc })
            .FirstOrDefaultAsync();
        return lead is null ? NotFound() : Ok(lead);
    }

    [HttpPost]
    [RequirePermission("leads.create")]
    public async Task<IActionResult> Create(LeadRequest request)
    {
        if (request.StatusId is 5 or 6 && (!User.IsInRole("Admin") || string.IsNullOrWhiteSpace(request.OverrideReason)))
            return BadRequest(new { message = "Create the lead with an open status, then request Won/Lost approval. Admin override requires a reason." });
        if (!await db.LeadSources.AnyAsync(s => s.Id == request.SourceId))
            return BadRequest(new { message = "Invalid source." });
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2 || string.IsNullOrWhiteSpace(request.Phone))
            return BadRequest(new { message = "Enter a name of at least 2 characters and a valid phone number." });
        if (request.ProjectAmount < 0) return BadRequest(new { message = "Project amount cannot be negative." });
        if (request.StatusId.HasValue && !await db.LeadStatuses.AnyAsync(s => s.Id == request.StatusId))
            return BadRequest(new { message = "Invalid status." });
        var userId = LeadAccess.UserId(User);
        var lead = new Lead
        {
            Name = request.Name.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email?.Trim(),
            CompanyName = request.CompanyName?.Trim(),
            Address = request.Address?.Trim(),
            LeadGivenBy = string.IsNullOrWhiteSpace(request.LeadGivenBy) ? User.Identity?.Name ?? "Employee" : request.LeadGivenBy.Trim(),
            ProjectAmount = request.ProjectAmount,
            Requirement = request.Requirement?.Trim(),
            SourceId = request.SourceId,
            StatusId = request.StatusId ?? 1,
            AssignedToId = userId,
            CreatedById = userId
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        lead.CustomerCode = $"CUS-{lead.CreatedAtUtc.Year}-{lead.Id:D6}";
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId, Action = "Create", TableName = "Leads", RecordId = lead.Id,
            ChangedFields = request.StatusId is 5 or 6 ? "Admin terminal override: " + request.OverrideReason!.Trim() : null
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return CreatedAtAction(nameof(Get), new { id = lead.Id }, new { lead.Id, lead.CustomerCode });
    }

    [HttpPut("{id:long}")]
    [RequirePermission("leads.edit")]
    public async Task<IActionResult> Update(long id, LeadRequest request)
    {
        var lead = await (await records.LeadsAsync(User, Action)).FirstOrDefaultAsync(l => l.Id == id);
        if (lead is null) return NotFound();
        if (!ConcurrencyTokens.Apply(db, lead, request.RowVersion))
            return BadRequest(new { message = "A valid row version is required. Reload this record." });
        if (request.StatusId.HasValue && request.StatusId != lead.StatusId)
            return BadRequest(new { message = "Use the status or approval action to change lead status." });
        if (!await db.LeadSources.AnyAsync(s => s.Id == request.SourceId))
            return BadRequest(new { message = "Invalid source." });
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2 || string.IsNullOrWhiteSpace(request.Phone))
            return BadRequest(new { message = "Enter a name of at least 2 characters and a valid phone number." });
        if (request.ProjectAmount < 0) return BadRequest(new { message = "Project amount cannot be negative." });
        lead.Name = request.Name.Trim();
        lead.Phone = request.Phone.Trim();
        lead.Email = request.Email?.Trim();
        lead.CompanyName = request.CompanyName?.Trim();
        lead.Address = request.Address?.Trim();
        lead.LeadGivenBy = string.IsNullOrWhiteSpace(request.LeadGivenBy) ? lead.LeadGivenBy : request.LeadGivenBy.Trim();
        lead.ProjectAmount = request.ProjectAmount;
        lead.Requirement = request.Requirement?.Trim();
        lead.SourceId = request.SourceId;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        lead.ModifiedByUserId = LeadAccess.UserId(User);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = lead.ModifiedByUserId.Value, Action = "Update", TableName = "Leads", RecordId = id,
            ChangedFields = "Customer details"
        });
        await db.SaveChangesAsync();
        Response.Headers["X-Row-Version"] = Convert.ToBase64String(lead.RowVersion);
        return NoContent();
    }

    [HttpPost("{id:long}/assign")]
    [RequirePermission("leads.assign")]
    public async Task<IActionResult> Assign(long id, AssignLeadRequest request)
    {
        var lead = await (await records.LeadsAsync(User, Action)).FirstOrDefaultAsync(l => l.Id == id);
        if (lead is null) return NotFound();
        if (!ConcurrencyTokens.Apply(db, lead, request.RowVersion))
            return BadRequest(new { message = "A valid row version is required. Reload this record." });
        var userId = LeadAccess.UserId(User);
        var assignee = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive);
        if (assignee is null) return BadRequest(new { message = "Employee not found." });
        if (!await (await records.EmployeesAsync(User, Action)).AnyAsync(x => x.Id == assignee.Id))
            return Forbid();
        if (lead.AssignedToId == request.UserId) return NoContent();

        db.LeadAssignments.Add(new LeadAssignment
        {
            LeadId = id, PreviousUserId = lead.AssignedToId,
            AssignedToId = request.UserId, AssignedById = userId
        });
        lead.AssignedToId = request.UserId;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId, Action = "Assign", TableName = "Leads", RecordId = id,
            ChangedFields = $"AssignedToId: {request.UserId}"
        });
        lead.UpdatedAtUtc = DateTime.UtcNow;
        var pendingFollowUps = await db.LeadFollowUps
            .Where(f => f.LeadId == id && f.CompletedAtUtc == null).ToListAsync();
        foreach (var followUp in pendingFollowUps)
            followUp.AssignedToId = request.UserId;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:long}/status")]
    [RequirePermission("leads.status")]
    public async Task<IActionResult> ChangeStatus(long id, ChangeLeadStatusRequest request)
    {
        if (request.StatusId is 5 or 6 && (!User.IsInRole("Admin") || string.IsNullOrWhiteSpace(request.OverrideReason)))
            return BadRequest(new { message = "Request approval for Won/Lost. Admin override requires a reason." });
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var lead = await (await records.LeadsAsync(User, Action)).FirstOrDefaultAsync(l => l.Id == id);
        if (lead is null) return NotFound();
        if (!ConcurrencyTokens.Apply(db, lead, request.RowVersion))
            return BadRequest(new { message = "A valid row version is required. Reload this record." });
        if (!await db.LeadStatuses.AnyAsync(s => s.Id == request.StatusId))
            return BadRequest(new { message = "Invalid status." });
        if (lead.StatusId == request.StatusId) return NoContent();
        if (request.StatusId is 5 or 6)
        {
            var pending = await db.LeadStatusApprovals.Where(x => x.LeadId == id && x.State == "Pending").ToListAsync();
            foreach (var approval in pending)
            {
                approval.State = "Rejected"; approval.DecidedById = LeadAccess.UserId(User);
                approval.DecidedAtUtc = DateTime.UtcNow; approval.Comment = "Superseded by an audited Admin override.";
            }
        }
        db.LeadStatusChanges.Add(new LeadStatusChange
        {
            LeadId = id, OldStatusId = lead.StatusId,
            NewStatusId = request.StatusId, ChangedById = LeadAccess.UserId(User)
        });
        lead.StatusId = request.StatusId;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = "Status", TableName = "Leads", RecordId = id,
            ChangedFields = $"StatusId: {request.StatusId}" + (request.StatusId is 5 or 6 ? "; Admin override: " + request.OverrideReason!.Trim() : "")
        });
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpGet("{id:long}/history")]
    [RequirePermission("leads.view")]
    public async Task<IActionResult> History(long id)
    {
        if (!await (await records.LeadsAsync(User, Action)).AnyAsync(l => l.Id == id))
            return NotFound();
        return Ok(new
        {
            assignments = await db.LeadAssignments.AsNoTracking().Where(a => a.LeadId == id)
                .OrderByDescending(a => a.AssignedAtUtc).ToListAsync(),
            statusChanges = await db.LeadStatusChanges.AsNoTracking().Where(s => s.LeadId == id)
                .OrderByDescending(s => s.ChangedAtUtc).ToListAsync(),
            followUps = await db.LeadFollowUps.AsNoTracking().Where(f => f.LeadId == id)
                .OrderByDescending(f => f.CreatedAtUtc)
                .Select(f => new { f.Id, f.Notes, f.DueAtUtc, f.CompletedAtUtc, f.AssignedToId, f.CreatedAtUtc })
                .ToListAsync()
        });
    }
}
