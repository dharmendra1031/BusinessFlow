using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1AccessTests
{
    [Theory]
    [InlineData(DataScope.Self, 1)]
    [InlineData(DataScope.DirectReports, 2)]
    [InlineData(DataScope.ReportingSubtree, 3)]
    [InlineData(DataScope.Team, 3)]
    [InlineData(DataScope.Department, 4)]
    [InlineData(DataScope.Organization, 5)]
    public async Task Lead_scope_includes_only_the_authorized_people(DataScope scope, int expected)
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var departmentA = new Department { Name = "Engineering" };
        var departmentB = new Department { Name = "Sales" };
        db.Departments.AddRange(departmentA, departmentB);
        await db.SaveChangesAsync();
        var me = new ApplicationUser { FullName = "Me", DepartmentId = departmentA.Id };
        var direct = new ApplicationUser { FullName = "Direct", DepartmentId = departmentA.Id };
        var grandchild = new ApplicationUser { FullName = "Grandchild", DepartmentId = departmentA.Id };
        var colleague = new ApplicationUser { FullName = "Colleague", DepartmentId = departmentA.Id };
        var outside = new ApplicationUser { FullName = "Outside", DepartmentId = departmentB.Id };
        db.Users.AddRange(me, direct, grandchild, colleague, outside);
        await db.SaveChangesAsync();
        direct.ManagerId = me.Id;
        grandchild.ManagerId = direct.Id;
        var alpha = new Team { Name = "Alpha", DepartmentId = departmentA.Id };
        db.Teams.Add(alpha);
        await db.SaveChangesAsync();
        foreach (var person in new[] { me, grandchild, colleague })
            db.TeamMemberships.Add(new TeamMembership { TeamId = alpha.Id, UserId = person.Id });
        foreach (var person in new[] { me, direct, grandchild, colleague, outside })
            db.Leads.Add(new Lead { Name = person.FullName, Phone = "1234567890", SourceId = 1,
                StatusId = 1, AssignedToId = person.Id, CreatedById = outside.Id });
        await db.SaveChangesAsync();
        var administration = new RoleAdministrationService(db);
        var roleId = await administration.CreateRoleAsync("Scoped", me.Id);
        await administration.SetGrantAsync(roleId, "leads.view", scope, me.Id);
        var principal = Phase1PermissionTests.Principal(me.Id, "Scoped");
        var access = new RecordAccessService(db, new RolePermissionService(db));
        var leads = await access.LeadsAsync(principal, "leads.view");
        Assert.Equal(expected, await leads.CountAsync());
        if (scope != DataScope.Organization)
            Assert.DoesNotContain(outside.Id, await leads.Select(x => x.AssignedToId).ToListAsync());
        Assert.Empty(await (await access.LeadsAsync(principal, "leads.edit")).ToListAsync());
        if (scope == DataScope.Team)
        {
            await administration.SetGrantAsync(roleId, "leads.edit", DataScope.Self, me.Id);
            Assert.Equal(1, await (await access.LeadsAsync(principal, "leads.edit")).CountAsync());
            await new OrganizationService(db).TransferAsync(me.Id, departmentA.Id, [], me.Id);
            Assert.Equal(1, await (await access.LeadsAsync(principal, "leads.view")).CountAsync());
        }
    }

    [Fact]
    public async Task A_wide_read_role_does_not_expand_another_roles_edit_scope()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var person = new ApplicationUser { FullName = "Editor" };
        var outsider = new ApplicationUser { FullName = "Outside" };
        db.Users.AddRange(person, outsider);
        await db.SaveChangesAsync();
        db.Leads.AddRange(
            new Lead { Name = "Own", Phone = "1234567890", SourceId = 1, StatusId = 1, AssignedToId = person.Id, CreatedById = outsider.Id },
            new Lead { Name = "Other", Phone = "1234567891", SourceId = 1, StatusId = 1, AssignedToId = outsider.Id, CreatedById = outsider.Id });
        await db.SaveChangesAsync();
        var administration = new RoleAdministrationService(db);
        var roleId = await administration.CreateRoleAsync("Auditor", person.Id);
        await administration.SetGrantAsync(roleId, "leads.view", DataScope.Organization, person.Id);
        var access = new RecordAccessService(db, new RolePermissionService(db));
        var principal = Phase1PermissionTests.Principal(person.Id, "Auditor", "Employee");
        Assert.Equal(2, await (await access.LeadsAsync(principal, "leads.view")).CountAsync());
        Assert.Equal(1, await (await access.LeadsAsync(principal, "leads.edit")).CountAsync());
    }
}
