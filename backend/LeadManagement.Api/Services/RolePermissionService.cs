using System.Security.Claims;
using LeadManagement.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Services;

public sealed class RolePermissionService(AppDbContext db)
{
    public static readonly string[] Codes =
    [
        "dashboard.view", "leads.view", "leads.create", "leads.edit", "leads.assign",
        "followups.view", "followups.manage", "projects.view", "reports.view",
        "employees.view"
    ];

    private static readonly string[] EmployeeRestricted = ["leads.assign", "employees.view"];

    public static bool IsBaselineAllowed(string role, string permission) =>
        Codes.Contains(permission) &&
        (role is "Admin" or "Manager" ||
         (role == "Employee" && !EmployeeRestricted.Contains(permission)));

    public async Task<bool> AllowedAsync(ClaimsPrincipal principal, string permission)
    {
        if (principal.IsInRole("Admin")) return true;
        if (!principal.IsInRole("Manager") && !principal.IsInRole("Employee"))
            return false;
        var role = principal.IsInRole("Manager") ? "Manager" : "Employee";
        if (!IsBaselineAllowed(role, permission)) return false;
        var rule = await db.RolePermissionOverrides.AsNoTracking()
            .Where(x => x.RoleName == role && x.Permission == permission)
            .Select(x => (bool?)x.Enabled).FirstOrDefaultAsync();
        return rule ?? true;
    }

    public async Task<Dictionary<string, bool>> EffectiveAsync(ClaimsPrincipal principal)
    {
        var role = principal.IsInRole("Admin") ? "Admin" :
            principal.IsInRole("Manager") ? "Manager" :
            principal.IsInRole("Employee") ? "Employee" : "";
        var overrides = await db.RolePermissionOverrides.AsNoTracking()
            .Where(x => x.RoleName == role).ToDictionaryAsync(x => x.Permission, x => x.Enabled);
        return Codes.ToDictionary(code => code,
            code => IsBaselineAllowed(role, code) &&
                (!overrides.TryGetValue(code, out var enabled) || enabled));
    }

    public static string? ForRequest(HttpRequest request)
    {
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
