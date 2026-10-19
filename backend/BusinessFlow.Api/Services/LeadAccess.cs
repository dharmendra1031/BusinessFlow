using System.Security.Claims;
using BusinessFlow.Api.Models;

namespace BusinessFlow.Api.Services;

public static class LeadAccess
{
    public static long UserId(ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue("sub")!);

    public static IQueryable<Lead> VisibleTo(IQueryable<Lead> leads, ClaimsPrincipal user)
    {
        if (user.IsInRole("Admin")) return leads;

        var userId = UserId(user);
        if (user.IsInRole("Manager"))
            return leads.Where(l => l.CreatedById == userId || l.AssignedToId == userId ||
                                    (l.AssignedTo != null && l.AssignedTo.ManagerId == userId));

        return leads.Where(l => l.AssignedToId == userId);
    }
}
