using BusinessFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1MigrationTests
{
    [Fact]
    public async Task Existing_schema_upgrade_preserves_ids_records_and_role_restrictions()
    {
        await using var database = await Phase1TestDatabase.CreateAsync("20261019133000_AddRolePermissionOverrides");
        await using var db = database.CreateContext();
        // Seed using only baseline columns, before any Phase 1 migration exists.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AspNetRoles (Name, NormalizedName) VALUES ('Manager', 'MANAGER'), ('Employee', 'EMPLOYEE');
            INSERT INTO AspNetUsers (FullName, IsActive, UserName, NormalizedUserName, EmailConfirmed,
                PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
            VALUES ('Existing manager', 1, 'manager', 'MANAGER', 0, 0, 0, 1, 0),
                   ('Existing employee', 1, 'employee', 'EMPLOYEE', 0, 0, 0, 1, 0);
            UPDATE AspNetUsers SET ManagerId = 1 WHERE Id = 2;
            INSERT INTO AspNetUserRoles (UserId, RoleId) SELECT 1, Id FROM AspNetRoles WHERE Name = 'Manager';
            INSERT INTO AspNetUserRoles (UserId, RoleId) SELECT 2, Id FROM AspNetRoles WHERE Name = 'Employee';
            INSERT INTO Leads (Name, Phone, SourceId, StatusId, CreatedById, AssignedToId, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('Existing customer', '1234567890', 1, 5, 1, 2, SYSUTCDATETIME(), SYSUTCDATETIME());
            INSERT INTO DevelopmentProjects (LeadId, Title, Status, OwnerId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 'Existing project', 'Planning', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            INSERT INTO DevelopmentTasks (ProjectId, Title, Status, Priority, AssignedToId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 'Existing task', 'Todo', 'Medium', 2, SYSUTCDATETIME(), SYSUTCDATETIME());
            INSERT INTO RolePermissionOverrides (RoleName, Permission, Enabled) VALUES ('Employee', 'projects.view', 0);
            """);
        await db.Database.MigrateAsync();
        Assert.Equal(new long[] { 1, 2 }, await db.Users.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        Assert.Equal(1, (await db.Leads.SingleAsync()).Id);
        Assert.Equal(5, (await db.Leads.SingleAsync()).StatusId);
        Assert.Equal("Existing customer", (await db.Leads.SingleAsync()).Name);
        Assert.Equal(8, (await db.Leads.SingleAsync()).RowVersion.Length);
        Assert.Equal(8, (await db.DevelopmentProjects.SingleAsync()).RowVersion.Length);
        Assert.Equal(8, (await db.DevelopmentTasks.SingleAsync()).RowVersion.Length);
        Assert.Null((await db.Users.SingleAsync(x => x.Id == 2)).DepartmentId);
        var permission = new RolePermissionService(db);
        Assert.False(await permission.AllowedAsync(Phase1PermissionTests.Principal(2, "Employee"), "projects.view"));
        Assert.False(await permission.AllowedAsync(Phase1PermissionTests.Principal(2, "Employee"), "tasks.status"));
        Assert.True(await permission.AllowedAsync(Phase1PermissionTests.Principal(1, "Manager"), "leads.assign"));
        Assert.Empty(await db.LeadStatusApprovals.ToListAsync());
    }
}
