using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1OrganizationTests
{
    [Fact]
    public async Task Transfer_updates_department_and_memberships_and_audits_atomically()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var a = new Department { Name = "Sales" };
        var b = new Department { Name = "Delivery" };
        db.Departments.AddRange(a, b);
        await db.SaveChangesAsync();
        var oldTeam = new Team { Name = "Sales A", DepartmentId = a.Id };
        var newTeam = new Team { Name = "Delivery B", DepartmentId = b.Id };
        var person = new ApplicationUser { FullName = "Alice", DepartmentId = a.Id };
        db.Teams.AddRange(oldTeam, newTeam);
        db.Users.Add(person);
        await db.SaveChangesAsync();
        db.TeamMemberships.Add(new TeamMembership { TeamId = oldTeam.Id, UserId = person.Id });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db);
        await service.TransferAsync(person.Id, b.Id, [newTeam.Id], person.Id);
        Assert.Equal(b.Id, (await db.Users.SingleAsync()).DepartmentId);
        Assert.Equal(newTeam.Id, (await db.TeamMemberships.SingleAsync()).TeamId);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "Transfer").ToListAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TransferAsync(person.Id, a.Id, [newTeam.Id], person.Id));
        await using var verify = fixture.CreateContext();
        Assert.Equal(b.Id, (await verify.Users.SingleAsync()).DepartmentId);
        Assert.Equal(newTeam.Id, (await verify.TeamMemberships.SingleAsync()).TeamId);
    }

    [Fact]
    public async Task Hierarchy_rejects_self_cycles_and_inactive_managers()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var chief = new ApplicationUser { FullName = "Chief" };
        var manager = new ApplicationUser { FullName = "Manager" };
        var employee = new ApplicationUser { FullName = "Employee" };
        var inactive = new ApplicationUser { FullName = "Inactive", IsActive = false };
        db.Users.AddRange(chief, manager, employee, inactive);
        await db.SaveChangesAsync();
        var service = new OrganizationService(db);
        await service.SetManagerAsync(manager.Id, chief.Id, chief.Id);
        await service.SetManagerAsync(employee.Id, manager.Id, chief.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetManagerAsync(chief.Id, employee.Id, chief.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetManagerAsync(chief.Id, chief.Id, chief.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetManagerAsync(employee.Id, inactive.Id, chief.Id));
        await using var verify = fixture.CreateContext();
        Assert.Null((await verify.Users.FindAsync(chief.Id))!.ManagerId);
        Assert.Equal(manager.Id, (await verify.Users.FindAsync(employee.Id))!.ManagerId);
    }

    [Fact]
    public async Task Populated_departments_and_teams_cannot_be_disabled()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var department = new Department { Name = "Delivery" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var team = new Team { Name = "Platform", DepartmentId = department.Id };
        var person = new ApplicationUser { FullName = "Alice", DepartmentId = department.Id };
        db.Teams.Add(team);
        db.Users.Add(person);
        await db.SaveChangesAsync();
        db.TeamMemberships.Add(new TeamMembership { TeamId = team.Id, UserId = person.Id });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveTeamAsync(team.Id, "Platform", department.Id, false, person.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDepartmentAsync(department.Id, "Delivery", false, person.Id));
        await service.TransferAsync(person.Id, null, [], person.Id);
        await service.SaveTeamAsync(team.Id, "Platform", department.Id, false, person.Id);
        await service.SaveDepartmentAsync(department.Id, "Delivery", false, person.Id);
        Assert.False((await db.Departments.FindAsync(department.Id))!.IsActive);
        Assert.False((await db.Teams.FindAsync(team.Id))!.IsActive);
    }
}
