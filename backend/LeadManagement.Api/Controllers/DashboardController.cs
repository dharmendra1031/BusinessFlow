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
            activeLeads = await leads.CountAsync(l => l.StatusId <= 4 || l.StatusId == 7),
            closedLeads = await leads.CountAsync(l => l.StatusId == 5 || l.StatusId == 8),
            notInterestedLeads = await leads.CountAsync(l => l.StatusId == 6 || l.StatusId == 9),
            totalProjectAmount = await leads.SumAsync(l => l.ProjectAmount ?? 0m),
            totalUsers = User.IsInRole("Admin") ? await db.Users.CountAsync(u => u.IsActive) : 0,
            newLeads = await leads.CountAsync(l => l.StatusId == 1),
            wonLeads = await leads.CountAsync(l => l.StatusId == 5),
            lostLeads = await leads.CountAsync(l => l.StatusId == 6),
            overdueFollowUps = await db.LeadFollowUps.CountAsync(f =>
                leadIds.Contains(f.LeadId) && f.CompletedAtUtc == null && f.DueAtUtc < now),
            todayFollowUps = await db.LeadFollowUps.CountAsync(f =>
                leadIds.Contains(f.LeadId) && f.CompletedAtUtc == null &&
                f.DueAtUtc >= now.Date && f.DueAtUtc < now.Date.AddDays(1)),
            upcomingFollowUps = await db.LeadFollowUps.CountAsync(f =>
                leadIds.Contains(f.LeadId) && f.CompletedAtUtc == null &&
                f.DueAtUtc >= now.Date.AddDays(1)),
            userWise = User.IsInRole("Admin")
                ? await leads.GroupBy(l => new { l.AssignedToId, Name = l.AssignedTo == null ? "Unassigned" : l.AssignedTo.FullName })
                    .Select(g => new { userId = g.Key.AssignedToId, userName = g.Key.Name,
                        customers = g.Count(), amount = g.Sum(l => l.ProjectAmount ?? 0m) })
                    .OrderByDescending(g => g.customers).Take(100).ToListAsync()
                : null
        });
    }
}
