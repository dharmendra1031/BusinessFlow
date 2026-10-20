using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/lead-approvals")]
public sealed class LeadApprovalsController(AppDbContext db, LeadApprovalService approvals, RecordAccessService access) : ControllerBase
{
    [HttpGet]
    [RequirePermission("leads.approve")]
    public async Task<IActionResult> List(int page = 1, int pageSize = 20, string state = "Pending", CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            state is not ("Pending" or "Approved" or "Rejected")) return BadRequest();
        var leadIds = (await access.LeadsAsync(User, "leads.approve", ct)).Select(x => x.Id);
        var query = db.LeadStatusApprovals.AsNoTracking().Where(x => x.State == state && leadIds.Contains(x.LeadId));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.RequestedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("leads/{leadId:long}")]
    [RequirePermission("leads.view")]
    public async Task<IActionResult> History(long leadId, CancellationToken ct)
    {
        if (!await (await access.LeadsAsync(User, "leads.view", ct)).AnyAsync(x => x.Id == leadId, ct)) return NotFound();
        return Ok(await db.LeadStatusApprovals.AsNoTracking().Where(x => x.LeadId == leadId)
            .OrderByDescending(x => x.Id).Take(100).ToListAsync(ct));
    }

    private async Task<IActionResult> Execute(Func<Task> action)
    {
        try { await action(); return NoContent(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException e) when (!SqlConflicts.IsRetryable(e)) { return BadRequest(new { message = e.Message }); }
    }

    [HttpPost("leads/{leadId:long}")]
    [RequirePermission("leads.request")]
    public Task<IActionResult> Submit(long leadId, RequestLeadApproval request, CancellationToken ct) => Execute(async () =>
    { await approvals.RequestAsync(leadId, request.StatusId, request.RowVersion, User, ct); });

    [HttpPost("{id:long}/decision")]
    [RequirePermission("leads.approve")]
    public Task<IActionResult> Decide(long id, DecideLeadApproval request, CancellationToken ct) =>
        Execute(() => approvals.DecideAsync(id, request.Approve, request.Comment, User, ct));
}
