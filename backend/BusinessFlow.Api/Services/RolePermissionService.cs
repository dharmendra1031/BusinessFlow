using System.Security.Claims;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Services;

public sealed class RolePermissionService(AppDbContext db)
{
    public static readonly string[] Codes =
    [
        "dashboard.view", "leads.view", "leads.create", "leads.edit", "leads.assign",
        "followups.view", "followups.manage", "projects.view", "reports.view",
        "employees.view", "employees.lookup", "leads.status", "leads.request", "leads.approve",
        "projects.create", "projects.edit", "tasks.create", "tasks.edit", "tasks.status",
        "bugs.view", "bugs.create", "bugs.edit", "bugs.status", "bugs.close",
        "worklogs.view", "worklogs.post", "planning.view", "planning.manage",
        "reports.export", "reports.pdf", "customers.view", "employees.create", "employees.edit", "employees.disable",
        "employees.reset-password", "organization.manage", "roles.manage"
    ];

    private static readonly string[] EmployeeRestricted =
    [
        "leads.assign", "leads.approve", "employees.view", "projects.create", "projects.edit",
        "tasks.create", "tasks.edit", "bugs.edit", "bugs.close", "planning.manage", "customers.view"
    ];

    public static bool AdminOnly(string permission) => permission is
        "employees.create" or "employees.edit" or "employees.disable" or "employees.reset-password" or
        "organization.manage" or "roles.manage";

    public static bool IsBaselineAllowed(string role, string permission) =>
        Codes.Contains(permission) &&
        (role == "Admin" || (!AdminOnly(permission) &&
         (role == "Manager" || role == "Employee" && !EmployeeRestricted.Contains(permission))));

    public static string? Parent(string permission) => permission switch
    {
        "leads.status" or "leads.request" or "leads.approve" => "leads.edit",
        "projects.create" or "projects.edit" or "tasks.create" or "tasks.edit" or "tasks.status" or
        "bugs.view" or "bugs.create" or "bugs.edit" or "bugs.status" or "bugs.close" or
        "worklogs.view" or "worklogs.post" or "planning.view" or "planning.manage" => "projects.view",
        "reports.export" or "reports.pdf" => "reports.view",
        _ => null
    };

    public async Task<bool> AllowedAsync(ClaimsPrincipal principal, string permission)
    {
        return (await ScopesAsync(principal, permission)).Length > 0;
    }

    public async Task<Dictionary<string, bool>> EffectiveAsync(ClaimsPrincipal principal)
    {
        var grants = await ResolveAsync(principal);
        return grants.ToDictionary(x => x.Key, x => x.Value.Length > 0);
    }

    public async Task<DataScope[]> ScopesAsync(ClaimsPrincipal principal, string permission)
    {
        if (!Codes.Contains(permission)) return [];
        return (await ResolveAsync(principal))[permission];
    }

    public async Task<DataScope[]> ManagementScopesAsync(ClaimsPrincipal principal, string permission)
    {
        if (!Codes.Contains(permission)) return [];
        return (await ResolveAsync(principal, management: true))[permission];
    }

    private async Task<Dictionary<string, DataScope[]>> ResolveAsync(ClaimsPrincipal principal, bool management = false)
    {
        var empty = Codes.ToDictionary(code => code, _ => Array.Empty<DataScope>());
        if (principal.Identity?.IsAuthenticated != true) return empty;
        if (principal.IsInRole("Admin")) return Codes.ToDictionary(code => code, _ => new[] { DataScope.Organization });
        var names = principal.Claims.Where(x => x.Type is "role" or ClaimTypes.Role).Select(x => x.Value).Distinct().ToArray();
        var userId = LeadAccess.UserId(principal);
        var restrictions = await db.UserPermissionRestrictions.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => x.Permission).ToListAsync();
        var overrides = await db.RolePermissionOverrides.AsNoTracking().Where(x => names.Contains(x.RoleName) && !x.Enabled)
            .ToListAsync();
        var grants = await (from grant in db.RolePermissionGrants.AsNoTracking()
            join role in db.Roles on grant.RoleId equals role.Id
            where names.Contains(role.Name!) select new { grant.Permission, grant.Scope, Role = role.Name }).ToListAsync();
        foreach (var code in Codes)
        {
            var parent = Parent(code);
            if (restrictions.Contains(code) || parent is not null && restrictions.Contains(parent)) continue;
            var scopes = new HashSet<DataScope>();
            foreach (var role in names)
            {
                if (overrides.Any(x => x.RoleName == role && (x.Permission == code || x.Permission == parent))) continue;
                if (IsBaselineAllowed(role, code) && (!management || role != "Employee"))
                    scopes.Add(role == "Manager" ? DataScope.DirectReports : DataScope.Self);
                foreach (var grant in grants.Where(x => x.Role == role && x.Permission == code && !AdminOnly(code)))
                    scopes.Add(grant.Scope);
            }
            empty[code] = scopes.OrderBy(x => x).ToArray();
        }
        return empty;
    }

    public static string? ForRequest(HttpRequest request)
    {
        var permission = request.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RequirePermissionAttribute>();
        if (permission is not null) return permission.Code;
        var path = request.Path.Value ?? "";
        var method = request.Method;
        if (path.StartsWith("/api/leads/", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("/followups", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsGet(method) ? "followups.view" : "followups.manage";
        if (path.Equals("/api/leads", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/leads/", StringComparison.OrdinalIgnoreCase))
        {
            if (HttpMethods.IsGet(method)) return "leads.view";
            if (path.EndsWith("/assign", StringComparison.OrdinalIgnoreCase)) return "leads.assign";
            if (HttpMethods.IsPost(method)) return "leads.create";
            return "leads.edit";
        }
        if (path.StartsWith("/api/followups", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsGet(method) ? "followups.view" : "followups.manage";
        if (path.StartsWith("/api/dashboard", StringComparison.OrdinalIgnoreCase))
            return "dashboard.view";
        if (path.StartsWith("/api/reports", StringComparison.OrdinalIgnoreCase))
            return "reports.view";
        if (path.StartsWith("/api/projects", StringComparison.OrdinalIgnoreCase))
            return "projects.view";
        if (path.StartsWith("/api/employees", StringComparison.OrdinalIgnoreCase))
            return "employees.view";
        return null;
    }
}
