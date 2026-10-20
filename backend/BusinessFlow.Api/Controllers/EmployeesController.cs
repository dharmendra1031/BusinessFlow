using BusinessFlow.Api.Services;
using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/employees")]
public sealed class EmployeesController(AppDbContext db, UserManager<ApplicationUser> users, RecordAccessService records) : ControllerBase
{
    private string Action => RolePermissionService.ForRequest(Request) ?? "employees.view";
    [HttpGet]
    [RequirePermission("employees.view")]
    public async Task<IActionResult> List(string? search = null, string? role = null, bool? isActive = null,
        long? managerId = null, long? departmentId = null, long? teamId = null, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!ValidPage(page, pageSize) || search?.Length > 150 || role?.Length > 80) return BadRequest();
        var query = (await records.EmployeesAsync(User, Action, ct)).AsNoTracking();
        query = Search(query, search);
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive);
        if (managerId.HasValue) query = query.Where(x => x.ManagerId == managerId);
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId);
        if (teamId.HasValue) query = query.Where(x => db.TeamMemberships.Any(m => m.TeamId == teamId && m.UserId == x.Id));
        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleUsers = from membership in db.UserRoles join identityRole in db.Roles on membership.RoleId equals identityRole.Id
                where identityRole.Name == role select membership.UserId;
            query = query.Where(x => roleUsers.Contains(x.Id));
        }
        return await Page(query, page, pageSize, ct);
    }

    private static bool ValidPage(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= 200 &&
        (long)(page - 1) * pageSize <= int.MaxValue;

    private static IQueryable<ApplicationUser> Search(IQueryable<ApplicationUser> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;
        var term = search.Trim();
        return query.Where(x => x.FullName.Contains(term) || x.Email != null && x.Email.Contains(term));
    }

    private async Task<IActionResult> Page(IQueryable<ApplicationUser> query, int page, int pageSize, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var employees = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.FullName, x.Email, x.PhoneNumber, x.IsActive, x.ManagerId, x.DepartmentId }).ToListAsync(ct);
        var ids = employees.Select(x => x.Id).ToArray();
        var memberships = await (from member in db.UserRoles join role in db.Roles on member.RoleId equals role.Id
            where ids.Contains(member.UserId) select new { member.UserId, role.Name }).ToListAsync(ct);
        var roles = memberships.ToLookup(x => x.UserId, x => x.Name);
        var items = employees.Select(x => new { x.Id, x.FullName, x.Email, x.PhoneNumber, x.IsActive, x.ManagerId,
            x.DepartmentId, roles = roles[x.Id].ToArray() });
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("lookup")]
    [RequirePermission("employees.lookup")]
    public async Task<IActionResult> Lookup(string action = "leads.assign", string? search = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        string[] supported = ["leads.assign", "projects.create", "projects.edit", "tasks.create", "tasks.edit",
            "bugs.create", "bugs.edit", "reports.view", "organization.manage", "roles.manage", "employees.create", "employees.edit", "employees.view"];
        if (!supported.Contains(action) || !ValidPage(page, pageSize) || search?.Length > 150) return BadRequest();
        var query = Search((await records.EmployeesAsync(User, action, ct)).AsNoTracking().Where(x => x.IsActive), search);
        // Directory administration needs full employee details. Assignment and report lookups do not.
        if (action is "organization.manage" or "roles.manage" or "employees.create" or "employees.edit" or "employees.view")
            return await Page(query, page, pageSize, ct);

        var total = await query.CountAsync(ct);
        var results = query.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize);
        if (action == "reports.view")
        {
            var reportUsers = await results.Select(x => new { x.Id, x.FullName, x.IsActive }).ToListAsync(ct);
            return Ok(new { total, page, pageSize, items = reportUsers });
        }
        var assignees = await results.Select(x => new { x.Id, x.FullName, x.Email, x.IsActive, x.ManagerId })
            .ToListAsync(ct);
        return Ok(new { total, page, pageSize, items = assignees });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [RequirePermission("employees.create")]
    public async Task<IActionResult> Create(CreateEmployeeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
            return BadRequest(new { message = "Employee name must contain at least 2 characters." });
        if (request.Role is not ("Manager" or "Employee"))
            return BadRequest(new { message = "Role must be Manager or Employee." });
        if (request.ManagerId is long managerId)
        {
            var manager = await users.FindByIdAsync(managerId.ToString());
            if (manager is null || !manager.IsActive || !await users.IsInRoleAsync(manager, "Manager"))
                return BadRequest(new { message = "Manager not found." });
        }

        var user = new ApplicationUser
        {
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            ManagerId = request.ManagerId,
            LockoutEnabled = true
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return BadRequest(new { errors = created.Errors.Select(e => e.Description) });
        var assigned = await users.AddToRoleAsync(user, request.Role);
        if (!assigned.Succeeded)
            return StatusCode(500, new { message = "Could not assign employee role." });
        await transaction.CommitAsync();
        return Created("/api/employees/" + user.Id, new { user.Id, user.FullName, user.Email });
    }
}
