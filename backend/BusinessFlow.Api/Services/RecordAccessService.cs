using System.Security.Claims;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Services;

public sealed class RecordAccessService(AppDbContext db, RolePermissionService permissions)
{
    public async Task<IQueryable<ApplicationUser>> EmployeesAsync(ClaimsPrincipal user, string action,
        CancellationToken ct = default)
    {
        var scopes = await permissions.ScopesAsync(user, action);
        if (scopes.Length == 0) return db.Users.Where(_ => false);
        if (scopes.Contains(DataScope.Organization)) return db.Users;
        var id = LeadAccess.UserId(user);
        var departmentId = await db.Users.Where(x => x.Id == id).Select(x => x.DepartmentId).SingleOrDefaultAsync(ct);
        var direct = scopes.Contains(DataScope.DirectReports);
        var department = scopes.Contains(DataScope.Department) && departmentId.HasValue &&
            await db.Departments.AnyAsync(x => x.Id == departmentId && x.IsActive, ct);
        var subtree = new HashSet<long> { id };
        if (scopes.Contains(DataScope.ReportingSubtree))
        {
            var frontier = new[] { id };
            while (frontier.Length > 0)
            {
                var children = await db.Users.Where(x => x.ManagerId.HasValue && frontier.Contains(x.ManagerId.Value))
                    .Select(x => x.Id).ToArrayAsync(ct);
                frontier = children.Where(child => subtree.Add(child)).ToArray();
            }
        }
        var subtreeIds = subtree.ToArray();
        var team = scopes.Contains(DataScope.Team);
        var currentTeams = from member in db.TeamMemberships
            join unit in db.Teams on member.TeamId equals unit.Id
            join parent in db.Departments on unit.DepartmentId equals parent.Id
            where member.UserId == id && unit.IsActive && parent.IsActive && unit.DepartmentId == departmentId
            select unit.Id;
        var teamUserIds = from member in db.TeamMemberships
            join unit in db.Teams on member.TeamId equals unit.Id
            join person in db.Users on member.UserId equals person.Id
            where currentTeams.Contains(unit.Id) && person.IsActive && person.DepartmentId == unit.DepartmentId
            select person.Id;
        return db.Users.Where(person => person.Id == id ||
            direct && person.ManagerId == id || subtreeIds.Contains(person.Id) ||
            department && person.DepartmentId == departmentId || team && teamUserIds.Contains(person.Id));
    }

    public async Task<IQueryable<Lead>> LeadsAsync(ClaimsPrincipal user, string action, CancellationToken ct = default)
    {
        var scopes = await permissions.ScopesAsync(user, action);
        if (scopes.Length == 0) return db.Leads.Where(_ => false);
        if (scopes.Contains(DataScope.Organization)) return db.Leads;
        var people = (await EmployeesAsync(user, action, ct)).Select(x => x.Id);
        var id = LeadAccess.UserId(user);
        // Preserve the built-in Manager's existing access to records they created.
        var ownCreated = user.IsInRole("Manager");
        return db.Leads.Where(lead => lead.AssignedToId.HasValue && people.Contains(lead.AssignedToId.Value) ||
            ownCreated && lead.CreatedById == id);
    }

    public async Task<IQueryable<DevelopmentProject>> ProjectsAsync(ClaimsPrincipal user, string action,
        CancellationToken ct = default)
        => await ProjectQueryAsync(user, action, true, ct);

    public async Task<IQueryable<DevelopmentProject>> ManagedProjectsAsync(ClaimsPrincipal user, string action,
        CancellationToken ct = default)
        => await ProjectQueryAsync(user, action, false, ct);

    private async Task<IQueryable<DevelopmentProject>> ProjectQueryAsync(ClaimsPrincipal user, string action,
        bool includeAssignments, CancellationToken ct)
    {
        var scopes = includeAssignments ? await permissions.ScopesAsync(user, action)
            : await permissions.ManagementScopesAsync(user, action);
        if (scopes.Length == 0) return db.DevelopmentProjects.Where(_ => false);
        if (scopes.Contains(DataScope.Organization)) return db.DevelopmentProjects;
        var people = (await EmployeesAsync(user, action, ct)).Select(x => x.Id);
        var id = LeadAccess.UserId(user);
        var ownCreated = user.IsInRole("Manager");
        var assignedWork = includeAssignments && action is ("projects.view" or "tasks.status" or "bugs.view" or "bugs.create" or
            "bugs.status" or "worklogs.view" or "worklogs.post" or "planning.view");
        return db.DevelopmentProjects.Where(project => people.Contains(project.OwnerId) ||
            ownCreated && project.CreatedById == id || assignedWork &&
            (project.Tasks.Any(task => task.AssignedToId == id) ||
             db.ProjectBugs.Any(bug => bug.ProjectId == project.Id && bug.AssignedToId == id)));
    }
}
