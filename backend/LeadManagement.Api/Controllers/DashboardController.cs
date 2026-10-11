using LeadManagement.Api.Data;
using LeadManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var leads = LeadAccess.VisibleTo(db.Leads.AsNoTracking(), User);
        var leadIds = leads.Select(l => l.Id);
        var now = DateTime.UtcNow;
        return Ok(new
        {
            totalLeads = await leads.CountAsync(),
            newLeads = await leads.CountAsync(l => l.StatusId == 1),
            wonLeads = await leads.CountAsync(l => l.StatusId == 5),
            lostLeads = await leads.CountAsync(l => l.StatusId == 6),
            overdueFollowUps = await db.LeadFollowUps.CountAsync(f =>
                leadIds.Contains(f.LeadId) && f.CompletedAtUtc == null && f.DueAtUtc < now)
        });
    }
}
