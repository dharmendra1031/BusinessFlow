using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

public sealed record SaveLeadRoutingRequest(long SourceId, long TeamId, int MaxOpenLeads, bool IsActive);

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/lead-routing")]
public sealed class LeadRoutingController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.LeadRoutingRules.AsNoTracking().OrderBy(x => x.SourceId)
            .Select(x => new { x.Id, x.SourceId, x.TeamId, x.MaxOpenLeads, x.IsActive })
            .ToListAsync(ct));

    [HttpPost]
    public Task<IActionResult> Create(SaveLeadRoutingRequest request, CancellationToken ct) =>
        Save(null, request, ct);

    [HttpPut("{id:long}")]
    public Task<IActionResult> Update(long id, SaveLeadRoutingRequest request, CancellationToken ct) =>
        Save(id, request, ct);

    private async Task<IActionResult> Save(long? id, SaveLeadRoutingRequest request, CancellationToken ct)
    {
        if (request.MaxOpenLeads is < 1 or > 1000)
            return BadRequest(new { message = "Maximum open leads must be between 1 and 1000." });
        if (!await db.LeadSources.AnyAsync(x => x.Id == request.SourceId &&
            (!request.IsActive || x.IsActive), ct))
            return BadRequest(new { message = "Select an existing active lead source." });
        if (!await (from team in db.Teams
            join department in db.Departments on team.DepartmentId equals department.Id
            where team.Id == request.TeamId && (!request.IsActive || team.IsActive && department.IsActive)
            select team.Id).AnyAsync(ct))
            return BadRequest(new { message = "Select an active team in an active department." });

        var rule = id.HasValue ? await db.LeadRoutingRules.SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (id.HasValue && rule is null) return NotFound();
        if (await db.LeadRoutingRules.AnyAsync(x => x.SourceId == request.SourceId && x.Id != id, ct))
            return Conflict(new { message = "A routing rule already exists for this source." });

        var creating = rule is null;
        rule ??= new LeadRoutingRule();
        rule.SourceId = request.SourceId;
        rule.TeamId = request.TeamId;
        rule.MaxOpenLeads = request.MaxOpenLeads;
        rule.IsActive = request.IsActive;
        if (creating) db.LeadRoutingRules.Add(rule);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
            db.AuditLogs.Add(new AuditLog
            {
                UserId = LeadAccess.UserId(User),
                TableName = "LeadRoutingRules", RecordId = rule.Id,
                Action = creating ? "Create" : "Update",
                ChangedFields = $"Source={rule.SourceId}; Team={rule.TeamId}; MaxOpen={rule.MaxOpenLeads}; Active={rule.IsActive}"
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(new { rule.Id, rule.SourceId, rule.TeamId, rule.MaxOpenLeads, rule.IsActive });
        }
        catch (DbUpdateException error) when (UniqueViolation(error))
        {
            return Conflict(new { message = "A routing rule already exists for this source." });
        }
    }

    private static bool UniqueViolation(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 2601 or 2627 }) return true;
        return false;
    }
}
