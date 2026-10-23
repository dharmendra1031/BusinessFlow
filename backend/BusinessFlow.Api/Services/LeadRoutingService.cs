using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Services;

/// <summary>Routing runs inside the caller's SQL transaction, before committing a new lead.</summary>
public sealed class LeadRoutingService(AppDbContext db)
{
    // Terminal/closed statuses do not consume team capacity.
    private static readonly long[] OpenStatuses = [1, 2, 3, 4, 7];

    /// <summary>
    /// Returns null when routing is not enabled. Throws if enabled but no eligible
    /// employee has remaining capacity: never silently bypass an explicit rule.
    /// </summary>
    public async Task<long?> SelectAssigneeAsync(long sourceId, CancellationToken ct)
    {
        var rule = await db.LeadRoutingRules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SourceId == sourceId && x.IsActive, ct);
        if (rule is null) return null;

        var candidate = await (
            from membership in db.TeamMemberships
            join person in db.Users on membership.UserId equals person.Id
            join team in db.Teams on membership.TeamId equals team.Id
            join department in db.Departments on team.DepartmentId equals department.Id
            where membership.TeamId == rule.TeamId && person.IsActive &&
                team.IsActive && department.IsActive && person.DepartmentId == team.DepartmentId
            select new
            {
                person.Id,
                OpenCount = db.Leads.Count(lead =>
                    lead.AssignedToId == person.Id && OpenStatuses.Contains(lead.StatusId))
            })
            .Where(x => x.OpenCount < rule.MaxOpenLeads)
            .OrderBy(x => x.OpenCount).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        return candidate?.Id ?? throw new InvalidOperationException(
            "No eligible routing employee has remaining capacity.");
    }
}
