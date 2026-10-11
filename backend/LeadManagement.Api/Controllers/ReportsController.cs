using LeadManagement.Api.Data;
using LeadManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public sealed class ReportsController(AppDbContext db) : ControllerBase
{
    private static readonly string[] ReportTypes =
    [
        "customers", "user-customers", "active", "closed", "not-interested",
        "followups", "overdue", "project", "user-project", "created"
    ];

    [HttpGet]
    public async Task<IActionResult> Get(string type = "customers", DateTime? from = null,
        DateTime? to = null, long? userId = null, long? statusId = null, string? leadGivenBy = null)
    {
        if (!ReportTypes.Contains(type)) return BadRequest(new { message = "Unknown report." });
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return BadRequest(new { message = "Invalid date range." });

        var leads = LeadAccess.VisibleTo(db.Leads.AsNoTracking(), User);
        if (userId.HasValue) leads = leads.Where(l => l.AssignedToId == userId);
        if (statusId.HasValue) leads = leads.Where(l => l.StatusId == statusId);
        if (!string.IsNullOrWhiteSpace(leadGivenBy))
            leads = leads.Where(l => l.LeadGivenBy != null && l.LeadGivenBy.Contains(leadGivenBy));

        if (type == "followups" || type == "overdue")
        {
            var leadIds = leads.Select(l => l.Id);
            var followUps = db.LeadFollowUps.AsNoTracking().Where(f => leadIds.Contains(f.LeadId));
            if (type == "overdue")
                followUps = followUps.Where(f => f.CompletedAtUtc == null && f.DueAtUtc < DateTime.UtcNow);
            if (from.HasValue) followUps = followUps.Where(f => f.DueAtUtc >= from.Value.Date);
            if (to.HasValue) followUps = followUps.Where(f => f.DueAtUtc < to.Value.Date.AddDays(1));
            var total = await followUps.CountAsync();
            var rows = await followUps.OrderByDescending(f => f.DueAtUtc).Take(5000)
                .Select(f => new
                {
                    followUpId = f.Id, customerId = f.Lead.Id,
                    customerCode = f.Lead.CustomerCode, customerName = f.Lead.Name,
                    followUpDate = f.CompletedAtUtc, nextFollowUpDate = f.DueAtUtc,
                    remarks = f.Notes, userId = f.AssignedToId
                }).ToListAsync();
            return Ok(new { type, total, rows, truncated = total > rows.Count });
        }

        if (from.HasValue) leads = leads.Where(l => l.CreatedAtUtc >= from.Value.Date);
        if (to.HasValue) leads = leads.Where(l => l.CreatedAtUtc < to.Value.Date.AddDays(1));

        leads = type switch
        {
            "active" => leads.Where(l => l.StatusId <= 4 || l.StatusId == 7),
            "closed" => leads.Where(l => l.StatusId == 5 || l.StatusId == 8),
            "not-interested" => leads.Where(l => l.StatusId == 6 || l.StatusId == 9),
            _ => leads
        };

        if (type is "user-customers" or "user-project")
        {
            var total = await leads.Select(l => l.AssignedToId).Distinct().CountAsync();
            var rows = await leads.GroupBy(l => new { l.AssignedToId, Name = l.AssignedTo == null ? "" : l.AssignedTo.FullName })
                .Select(g => new
                {
                    userId = g.Key.AssignedToId, userName = g.Key.Name,
                    customerCount = g.Count(), projectAmount = g.Sum(l => l.ProjectAmount ?? 0m)
                }).OrderByDescending(g => g.customerCount).Take(5000).ToListAsync();
            return Ok(new { type, total, rows, truncated = total > rows.Count });
        }

        var count = await leads.CountAsync();
        var customers = await leads.OrderByDescending(l => l.CreatedAtUtc).Take(5000)
            .Select(l => new
            {
                customerId = l.Id, l.CustomerCode, customerName = l.Name, mobileNumber = l.Phone,
                l.CompanyName, l.Address, l.LeadGivenBy, l.ProjectAmount,
                leadStatus = l.Status.Name, l.LastFollowUpDate, l.NextFollowUpDate,
                createdByUserId = l.CreatedById, assignedToUserId = l.AssignedToId,
                l.CreatedAtUtc
            }).ToListAsync();
        return Ok(new { type, total = count, rows = customers, truncated = count > customers.Count });
    }
}
