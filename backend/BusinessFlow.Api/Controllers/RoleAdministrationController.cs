using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/roles")]
public sealed class RoleAdministrationController(AppDbContext db, RoleAdministrationService administration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var grants = await db.RolePermissionGrants.AsNoTracking().ToListAsync(ct);
        var members = await db.UserRoles.AsNoTracking().ToListAsync(ct);
        return Ok(new
        {
            roles = roles.Select(role => new
            {
                role.Id, role.Name, isBuiltIn = RoleAdministrationService.IsBuiltIn(role.Name),
                grants = grants.Where(x => x.RoleId == role.Id).Select(x => new { x.Permission, x.Scope }),
                memberIds = members.Where(x => x.RoleId == role.Id).Select(x => x.UserId)
            }),
            permissions = RolePermissionService.Codes.Where(x => !RolePermissionService.AdminOnly(x)),
            scopes = Enum.GetNames<DataScope>()
        });
    }

    private async Task<IActionResult> Execute(Func<Task<object>> operation)
    {
        try { return Ok(await operation()); }
        catch (Exception e) when (SqlConflicts.IsRetryable(e))
        { return Conflict(new { message = "Role settings changed concurrently. Reload and retry." }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (InvalidOperationException e) { return BadRequest(new { message = e.Message }); }
    }

    [HttpPost]
    public Task<IActionResult> Create(SaveRoleRequest request, CancellationToken ct) => Execute(async () =>
        new { id = await administration.CreateRoleAsync(request.Name, LeadAccess.UserId(User), ct) });

    [HttpPut("{id:long}")]
    public Task<IActionResult> Rename(long id, SaveRoleRequest request, CancellationToken ct) => Execute(async () =>
    {
        await administration.RenameRoleAsync(id, request.Name, LeadAccess.UserId(User), ct);
        return new { id };
    });

    [HttpDelete("{id:long}")]
    public Task<IActionResult> Delete(long id, CancellationToken ct) => Execute(async () =>
    {
        await administration.DeleteRoleAsync(id, LeadAccess.UserId(User), ct);
        return new { id };
    });

    [HttpPut("{id:long}/grants/{permission}")]
    public Task<IActionResult> Grant(long id, string permission, SaveGrantRequest request, CancellationToken ct) => Execute(async () =>
    {
        await administration.SetGrantAsync(id, permission, request.Scope, LeadAccess.UserId(User), ct);
        return new { id };
    });

    [HttpPut("{id:long}/members/{userId:long}")]
    public Task<IActionResult> Membership(long id, long userId, SetRoleMemberRequest request, CancellationToken ct) => Execute(async () =>
    {
        await administration.SetMemberAsync(id, userId, request.Assigned, LeadAccess.UserId(User), ct);
        return new { id };
    });

    [HttpGet("employees/{userId:long}/restrictions")]
    public async Task<IActionResult> Restrictions(long userId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId, ct)) return NotFound();
        return Ok(new { denied = await db.UserPermissionRestrictions.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => x.Permission).ToListAsync(ct) });
    }

    [HttpPut("employees/{userId:long}/restrictions/{permission}")]
    public Task<IActionResult> Restrict(long userId, string permission, SetRestrictionRequest request, CancellationToken ct) => Execute(async () =>
    {
        await administration.SetRestrictionAsync(userId, permission, request.Denied!.Value, LeadAccess.UserId(User), ct);
        return new { id = userId };
    });
}
