using System.Data;
using System.Security.Cryptography;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Services;

public sealed class RoleAdministrationService(AppDbContext db)
{
    public static bool IsBuiltIn(string? name) => name?.Trim().ToUpperInvariant() is "ADMIN" or "MANAGER" or "EMPLOYEE";

    private void Audit(string action, string entity, long id, long actor, string? detail = null) =>
        db.AuditLogs.Add(new AuditLog { Action = action, TableName = entity, RecordId = id, UserId = actor, ChangedFields = detail });

    private async Task<string> ValidateNameAsync(string name, long? id, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 80 || IsBuiltIn(name))
            throw new InvalidOperationException("Choose a custom role name of 2 to 80 characters.");
        var normalized = name.ToUpperInvariant();
        if (await db.Roles.AnyAsync(x => x.Id != id && x.NormalizedName == normalized, ct))
            throw new InvalidOperationException("A role with this name already exists.");
        return name;
    }

    public async Task<long> CreateRoleAsync(string name, long actor, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        name = await ValidateNameAsync(name, null, ct);
        var role = new IdentityRole<long>(name) { NormalizedName = name.ToUpperInvariant() };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        Audit("Create", "Roles", role.Id, actor, name);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return role.Id;
    }

    private async Task<IdentityRole<long>> CustomRoleAsync(long id, CancellationToken ct)
    {
        var role = await db.Roles.FindAsync([id], ct) ?? throw new KeyNotFoundException("Role not found.");
        if (IsBuiltIn(role.Name)) throw new InvalidOperationException("Built-in roles are protected.");
        return role;
    }

    public async Task RenameRoleAsync(long id, string name, long actor, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var role = await CustomRoleAsync(id, ct);
        name = await ValidateNameAsync(name, id, ct);
        role.Name = name;
        role.NormalizedName = name.ToUpperInvariant();
        Audit("Rename", "Roles", id, actor, name);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task DeleteRoleAsync(long id, long actor, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var role = await CustomRoleAsync(id, ct);
        if (await db.UserRoles.AnyAsync(x => x.RoleId == id, ct))
            throw new InvalidOperationException("Remove role memberships before deleting this role.");
        db.RolePermissionGrants.RemoveRange(await db.RolePermissionGrants.Where(x => x.RoleId == id).ToListAsync(ct));
        db.Roles.Remove(role);
        Audit("Delete", "Roles", id, actor, role.Name);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task SetGrantAsync(long roleId, string permission, DataScope? scope, long actor, CancellationToken ct = default)
    {
        if (!RolePermissionService.Codes.Contains(permission) || RolePermissionService.AdminOnly(permission) ||
            scope.HasValue && !Enum.IsDefined(scope.Value))
            throw new InvalidOperationException("Choose a supported action and scope.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await CustomRoleAsync(roleId, ct);
        var grant = await db.RolePermissionGrants.SingleOrDefaultAsync(x => x.RoleId == roleId && x.Permission == permission, ct);
        if (scope.HasValue)
        {
            if (grant is null) db.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = roleId, Permission = permission, Scope = scope.Value });
            else grant.Scope = scope.Value;
        }
        else if (grant is not null) db.RolePermissionGrants.Remove(grant);
        Audit(scope.HasValue ? "Grant" : "Revoke", "RoleGrants", roleId, actor, permission + ":" + scope);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task SetRestrictionAsync(long userId, string permission, bool denied, long actor, CancellationToken ct = default)
    {
        if (!RolePermissionService.Codes.Contains(permission) || RolePermissionService.AdminOnly(permission))
            throw new InvalidOperationException("Choose a supported employee action.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await db.Users.AnyAsync(x => x.Id == userId, ct)) throw new KeyNotFoundException("Employee not found.");
        if (await (from membership in db.UserRoles join role in db.Roles on membership.RoleId equals role.Id
            where membership.UserId == userId && role.Name == "Admin" select membership).AnyAsync(ct))
            throw new InvalidOperationException("Administrator access is protected.");
        var restriction = await db.UserPermissionRestrictions.SingleOrDefaultAsync(x => x.UserId == userId && x.Permission == permission, ct);
        if (denied && restriction is null) db.UserPermissionRestrictions.Add(new UserPermissionRestriction { UserId = userId, Permission = permission });
        else if (!denied && restriction is not null) db.UserPermissionRestrictions.Remove(restriction);
        Audit(denied ? "Deny" : "Restore", "UserPermissions", userId, actor, permission);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task SetMemberAsync(long roleId, long userId, bool assigned, long actor, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await CustomRoleAsync(roleId, ct);
        var user = await db.Users.FindAsync([userId], ct) ?? throw new KeyNotFoundException("Employee not found.");
        var member = await db.UserRoles.SingleOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId, ct);
        if (assigned && member is null) db.UserRoles.Add(new IdentityUserRole<long> { UserId = userId, RoleId = roleId });
        else if (!assigned && member is not null) db.UserRoles.Remove(member);
        else return;
        user.SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Audit(assigned ? "AssignRole" : "RemoveRole", "Users", userId, actor, roleId.ToString());
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
