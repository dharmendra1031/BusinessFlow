using System.Security.Claims;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1PermissionTests
{
    internal static ClaimsPrincipal Principal(long id, params string[] roles) => new(new ClaimsIdentity(
        new[] { new Claim("sub", id.ToString()) }.Concat(roles.Select(role => new Claim("role", role))),
        "Test", "name", "role"));

    [Fact]
    public async Task Custom_role_defaults_to_no_access_and_user_deny_overrides_all_grants()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var person = new ApplicationUser { FullName = "QA" };
        db.Users.Add(person);
        await db.SaveChangesAsync();
        var administration = new RoleAdministrationService(db);
        var roleId = await administration.CreateRoleAsync("Quality", person.Id);
        var principal = Principal(person.Id, "Quality", "Employee");
        var permission = new RolePermissionService(db);
        Assert.False(await permission.AllowedAsync(Principal(person.Id, "Quality"), "leads.view"));
        await administration.SetGrantAsync(roleId, "leads.view", DataScope.Department, person.Id);
        Assert.True(await permission.AllowedAsync(principal, "leads.view"));
        Assert.Equal(new[] { DataScope.Self, DataScope.Department },
            (await permission.ScopesAsync(principal, "leads.view")).OrderBy(x => x).ToArray());
        await administration.SetRestrictionAsync(person.Id, "leads.view", true, person.Id);
        Assert.False(await permission.AllowedAsync(principal, "leads.view"));
        Assert.Empty(await permission.ScopesAsync(principal, "leads.view"));
        await administration.SetRestrictionAsync(person.Id, "leads.view", false, person.Id);
        Assert.True(await permission.AllowedAsync(principal, "leads.view"));
        await administration.SetGrantAsync(roleId, "leads.view", null, person.Id);
        Assert.False(await permission.AllowedAsync(Principal(person.Id, "Quality"), "leads.view"));
        Assert.False(await permission.AllowedAsync(Principal(person.Id, "Admin"), "unknown.action"));
        Assert.True(await db.AuditLogs.CountAsync() >= 5);
    }

    [Fact]
    public async Task Role_administration_rejects_unknown_actions_invalid_scopes_and_protected_roles()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var admin = new IdentityRole<long>("Admin");
        db.Roles.Add(admin);
        await db.SaveChangesAsync();
        var service = new RoleAdministrationService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRoleAsync(" admin ", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameRoleAsync(admin.Id, "Owner", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRoleAsync(admin.Id, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetGrantAsync(admin.Id, "leads.view", DataScope.Self, 1));
        var roleId = await service.CreateRoleAsync("Quality", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetGrantAsync(roleId, "unknown.action", DataScope.Self, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetGrantAsync(roleId, "leads.view", (DataScope)99, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetGrantAsync(roleId, "organization.manage", DataScope.Organization, 1));
        Assert.Empty(await db.RolePermissionGrants.ToListAsync());
    }
}
