using LeadManagement.Api.Data;
using LeadManagement.Api.Models;
using LeadManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Controllers;

public sealed record SetRolePermissionRequest(bool Enabled);

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/permissions")]
public sealed class RolePermissionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var overrides = await db.RolePermissionOverrides.AsNoTracking().ToListAsync();
        var roles = new[] { "Manager", "Employee" }.Select(role => new
        {
            role,
            permissions = RolePermissionService.Codes.Select(code => new
            {
                code,
                supported = RolePermissionService.IsBaselineAllowed(role, code),
                enabled = RolePermissionService.IsBaselineAllowed(role, code) &&
                    !overrides.Any(x => x.RoleName == role && x.Permission == code && !x.Enabled)
            }).ToArray()
        });
        return Ok(new { roles });
    }

    [HttpPut("{role}/{permission}")]
    public async Task<IActionResult> Set(string role, string permission, SetRolePermissionRequest request)
    {
        if (role is not ("Manager" or "Employee") ||
            !RolePermissionService.Codes.Contains(permission) ||
            (request.Enabled && !RolePermissionService.IsBaselineAllowed(role, permission)))
            return BadRequest(new { message = "Permission cannot exceed the role's existing access." });

        var entry = await db.RolePermissionOverrides
            .FirstOrDefaultAsync(x => x.RoleName == role && x.Permission == permission);
        if (entry is null && request.Enabled || entry is { Enabled: false } && !request.Enabled)
            return NoContent();

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (request.Enabled)
        {
            db.RolePermissionOverrides.Remove(entry!);
        }
        else if (entry is null)
        {
            entry = new RolePermissionOverride
            {
                RoleName = role, Permission = permission, Enabled = false
            };
            db.RolePermissionOverrides.Add(entry);
        }
        else
        {
            entry.Enabled = false;
        }
        await db.SaveChangesAsync();

        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = request.Enabled ? "Allow" : "Deny",
            TableName = "RolePermissions", RecordId = entry!.Id,
            ChangedFields = role + ":" + permission
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/auth/permissions")]
public sealed class CurrentPermissionsController(RolePermissionService permissions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() =>
        Ok(new { permissions = await permissions.EffectiveAsync(User) });
}
