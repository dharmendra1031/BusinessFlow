using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

public sealed record SaveLeadSourceRequest(string Name, bool IsActive = true);

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/lead-sources")]
public sealed class LeadSourcesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.LeadSources.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.IsActive }).ToListAsync(ct));

    [HttpPost]
    public Task<IActionResult> Create(SaveLeadSourceRequest request, CancellationToken ct) =>
        Save(null, request, ct);

    [HttpPut("{id:long}")]
    public Task<IActionResult> Update(long id, SaveLeadSourceRequest request, CancellationToken ct) =>
        Save(id, request, ct);

    private async Task<IActionResult> Save(long? id, SaveLeadSourceRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 60)
            return BadRequest(new { message = "Lead source name must contain 2 to 60 characters." });

        LeadSource source;
        if (id.HasValue)
        {
            source = await db.LeadSources.SingleOrDefaultAsync(s => s.Id == id, ct)
                ?? new LeadSource();
            if (source.Id == 0) return NotFound(new { message = "Lead source not found." });
        }
        else
        {
            source = new LeadSource();
            db.LeadSources.Add(source);
        }

        if (await db.LeadSources.AsNoTracking().AnyAsync(s =>
                s.Name.ToUpper() == name.ToUpper() && (!id.HasValue || s.Id != id.Value), ct))
            return Conflict(new { message = "A lead source with this name already exists." });

        var previousName = source.Name;
        source.Name = name;
        source.IsActive = request.IsActive;

        // Save source ID first for auto-increment identities; the audit event is
        // committed in the same SQL transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
            db.AuditLogs.Add(new AuditLog
            {
                UserId = LeadAccess.UserId(User),
                Action = id.HasValue ? "Update" : "Create",
                TableName = "LeadSources",
                RecordId = source.Id,
                ChangedFields = $"Name: {previousName} -> {source.Name}; IsActive: {source.IsActive}"
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(new { source.Id, source.Name, source.IsActive });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "A lead source with this name already exists." });
        }
    }
}
