using System.Security.Claims;
using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Controllers;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class LeadPermissionsTests
{
    [Fact]
    public async Task Roles_and_assignments_are_enforced_in_sql_server()
    {
        var connection = Environment.GetEnvironmentVariable("CRM_TEST_CONNECTION")
            ?? throw new InvalidOperationException("CRM_TEST_CONNECTION is required.");

        Assert.StartsWith("BusinessFlow_CITests",
            new SqlConnectionStringBuilder(connection).InitialCatalog, StringComparison.Ordinal);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var admin = new ApplicationUser { FullName = "Admin", UserName = "admin@test.local" };
        var manager = new ApplicationUser { FullName = "Manager", UserName = "manager@test.local" };
        var employee = new ApplicationUser { FullName = "Employee", UserName = "employee@test.local" };
        var outsider = new ApplicationUser { FullName = "Other employee", UserName = "other@test.local" };

        db.Users.AddRange(admin, manager, employee, outsider);
        await db.SaveChangesAsync();
        employee.ManagerId = manager.Id;

        var ownLead = new Lead
        {
            Name = "Employee lead", Phone = "1000000001", SourceId = 1,
            StatusId = 1, AssignedToId = employee.Id, CreatedById = admin.Id
        };
        var outsideLead = new Lead
        {
            Name = "Outside lead", Phone = "1000000002", SourceId = 1,
            StatusId = 1, AssignedToId = outsider.Id, CreatedById = admin.Id
        };
        db.Leads.AddRange(ownLead, outsideLead);
        await db.SaveChangesAsync();

        Assert.True(admin.Id > 0 && employee.Id > 0 && ownLead.Id > 0);
        Assert.Equal(2, await LeadAccess.VisibleTo(db.Leads, Principal(admin.Id, "Admin")).CountAsync());
        Assert.Equal(1, await LeadAccess.VisibleTo(db.Leads, Principal(manager.Id, "Manager")).CountAsync());
        Assert.Equal(1, await LeadAccess.VisibleTo(db.Leads, Principal(employee.Id, "Employee")).CountAsync());
        Assert.DoesNotContain(outsideLead.Id, await LeadAccess.VisibleTo(
            db.Leads, Principal(employee.Id, "Employee")).Select(l => l.Id).ToListAsync());

        var employeeController = Controller(db, employee.Id, "Employee");
        Assert.IsType<NotFoundResult>(await employeeController.Get(outsideLead.Id));
        Assert.IsType<NotFoundResult>(await employeeController.Update(outsideLead.Id,
            new LeadRequest("Unauthorized", "999999", null, null, null, 1)));

        db.LeadFollowUps.AddRange(
            new LeadFollowUp
            {
                LeadId = ownLead.Id, AssignedToId = employee.Id, CreatedById = admin.Id,
                Notes = "Pending", DueAtUtc = DateTime.UtcNow.AddDays(1)
            },
            new LeadFollowUp
            {
                LeadId = ownLead.Id, AssignedToId = employee.Id, CreatedById = admin.Id,
                Notes = "Completed", DueAtUtc = DateTime.UtcNow.AddDays(-1),
                CompletedAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var managerController = Controller(db, manager.Id, "Manager");
        Assert.IsType<ForbidResult>(await managerController.Assign(ownLead.Id, new AssignLeadRequest(outsider.Id)));
        await db.Entry(ownLead).ReloadAsync();
        Assert.Equal(employee.Id, ownLead.AssignedToId);
        Assert.Empty(await db.LeadAssignments.ToListAsync());

        Assert.IsType<NoContentResult>(await managerController.Assign(ownLead.Id,
            new AssignLeadRequest(manager.Id)));
        await db.Entry(ownLead).ReloadAsync();
        Assert.Equal(manager.Id, ownLead.AssignedToId);
        Assert.Single(await db.LeadAssignments.ToListAsync());
        Assert.Equal(manager.Id, (await db.LeadFollowUps.SingleAsync(f => f.Notes == "Pending")).AssignedToId);
        Assert.Equal(employee.Id, (await db.LeadFollowUps.SingleAsync(f => f.Notes == "Completed")).AssignedToId);

        var createdResponse = await employeeController.Create(new LeadRequest(
            "New SRS customer", "1000000003", null, "Customer Co", "Project", 1,
            "Kuwait City", "Manager", 15000.500m, 7));
        Assert.IsType<CreatedAtActionResult>(createdResponse);
        var createdLead = await db.Leads.SingleAsync(l => l.Name == "New SRS customer");
        Assert.Equal($"CUS-{DateTime.UtcNow.Year}-{createdLead.Id:D6}", createdLead.CustomerCode);
        Assert.Equal("Manager", createdLead.LeadGivenBy);
        Assert.Equal(15000.500m, createdLead.ProjectAmount);
        Assert.Equal(7, createdLead.StatusId);

        var reports = new ReportsController(db, new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(employee.Id, "Employee") }
            }
        };
        var result = Assert.IsType<OkObjectResult>(await reports.Get());
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("New SRS customer", json);
        Assert.DoesNotContain("Outside lead", json);

        var followUps = new FollowUpsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(employee.Id, "Employee") }
            }
        };
        Assert.IsType<NotFoundResult>(await followUps.History(outsideLead.Id));
        Assert.IsType<OkObjectResult>(await followUps.History(createdLead.Id));
    }

    private static LeadsController Controller(AppDbContext db, long userId, string role) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(userId, role) }
            }
        };

    private static ClaimsPrincipal Principal(long userId, string role) =>
        new(new ClaimsIdentity(
            [new Claim("sub", userId.ToString()), new Claim(ClaimTypes.Role, role)],
            "Test"));
}
