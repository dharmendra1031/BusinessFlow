using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1ScopeSecurityTests
{
    private static async Task<long> IdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();

    [Fact]
    public async Task Organization_report_scope_does_not_expose_private_directory_fields()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var reader = await host.ClientAsync("Employee");
        using var outsider = await host.ClientAsync("Employee");
        var readerId = await IdAsync(reader);
        var outsiderId = await IdAsync(outsider);

        await using var db = database.CreateContext();
        var outside = await db.Users.SingleAsync(x => x.Id == outsiderId);
        outside.PhoneNumber = "+96555509999";
        await db.SaveChangesAsync();
        var roles = new RoleAdministrationService(db);
        var roleId = await roles.CreateRoleAsync("Report organization reader", outsiderId);
        await roles.SetGrantAsync(roleId, "reports.view", DataScope.Organization, outsiderId);
        await roles.SetMemberAsync(roleId, readerId, true, outsiderId);
        using var session = await host.ClientForUserAsync(readerId);

        var report = await session.GetFromJsonAsync<JsonElement>("/api/employees/lookup?action=reports.view&pageSize=100");
        Assert.Equal(2, report.GetProperty("total").GetInt32());
        var item = report.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetInt64() == outsiderId);
        Assert.True(item.TryGetProperty("fullName", out _));
        Assert.False(item.TryGetProperty("email", out _));
        Assert.False(item.TryGetProperty("phoneNumber", out _));
        Assert.False(item.TryGetProperty("roles", out _));
        Assert.False(item.TryGetProperty("departmentId", out _));
        Assert.False(item.TryGetProperty("managerId", out _));

        var directory = await session.GetFromJsonAsync<JsonElement>("/api/employees");
        Assert.Equal(1, directory.GetProperty("total").GetInt32());
        var directoryLookup = await session.GetFromJsonAsync<JsonElement>("/api/employees/lookup?action=employees.view");
        Assert.Equal(1, directoryLookup.GetProperty("total").GetInt32());
        var assignLookup = await session.GetFromJsonAsync<JsonElement>("/api/employees/lookup?action=leads.assign");
        Assert.Equal(0, assignLookup.GetProperty("total").GetInt32());

        await roles.SetRestrictionAsync(readerId, "employees.lookup", true, outsiderId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.GetAsync("/api/employees/lookup?action=reports.view")).StatusCode);
    }

    [Fact]
    public async Task Department_transfer_recomputes_lead_project_and_assignment_visibility()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var reader = await host.ClientAsync("Employee");
        var readerId = await IdAsync(reader);
        await using var db = database.CreateContext();
        var engineering = new Department { Name = "Engineering" };
        var sales = new Department { Name = "Sales" };
        db.Departments.AddRange(engineering, sales);
        await db.SaveChangesAsync();
        var me = await db.Users.SingleAsync(x => x.Id == readerId);
        me.DepartmentId = engineering.Id;
        var colleague = new ApplicationUser { FullName = "Engineering colleague", DepartmentId = engineering.Id, IsActive = true };
        var outside = new ApplicationUser { FullName = "Sales colleague", DepartmentId = sales.Id, IsActive = true };
        db.Users.AddRange(colleague, outside);
        await db.SaveChangesAsync();
        var people = new[] { me, colleague, outside };
        foreach (var person in people)
        {
            var lead = new Lead { Name = person.FullName, Phone = "1234567890",
                SourceId = 1, StatusId = 5, AssignedToId = person.Id, CreatedById = me.Id };
            db.Leads.Add(lead);
            await db.SaveChangesAsync();
            db.DevelopmentProjects.Add(new DevelopmentProject
            {
                LeadId = lead.Id, Title = person.FullName, OwnerId = person.Id, CreatedById = me.Id
            });
        }
        await db.SaveChangesAsync();

        var admin = new RoleAdministrationService(db);
        var roleId = await admin.CreateRoleAsync("Department access", readerId);
        foreach (var permission in new[] { "leads.view", "leads.assign", "projects.view" })
            await admin.SetGrantAsync(roleId, permission, DataScope.Department, readerId);
        await admin.SetMemberAsync(roleId, readerId, true, readerId);
        using var session = await host.ClientForUserAsync(readerId);

        static async Task<long[]> ItemIds(HttpClient client, string path)
        {
            var body = await client.GetFromJsonAsync<JsonElement>(path);
            return body.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("id").GetInt64()).ToArray();
        }

        var leads = await db.Leads.AsNoTracking().ToDictionaryAsync(x => x.AssignedToId!.Value, x => x.Id);
        var projects = await db.DevelopmentProjects.AsNoTracking().ToDictionaryAsync(x => x.OwnerId, x => x.Id);
        Assert.Equal(new[] { leads[me.Id], leads[colleague.Id] }.Order(), (await ItemIds(session, "/api/leads")).Order());
        Assert.Equal(new[] { projects[me.Id], projects[colleague.Id] }.Order(), (await ItemIds(session, "/api/projects")).Order());
        var assignees = await ItemIds(session, "/api/employees/lookup?action=leads.assign");
        Assert.Contains(colleague.Id, assignees);
        Assert.DoesNotContain(outside.Id, assignees);
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/api/leads/{leads[outside.Id]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/api/projects/{projects[outside.Id]}")).StatusCode);

        await new OrganizationService(db).TransferAsync(readerId, sales.Id, [], readerId);
        Assert.Equal(new[] { leads[me.Id], leads[outside.Id] }.Order(), (await ItemIds(session, "/api/leads")).Order());
        Assert.Equal(new[] { projects[me.Id], projects[outside.Id] }.Order(), (await ItemIds(session, "/api/projects")).Order());
        assignees = await ItemIds(session, "/api/employees/lookup?action=leads.assign");
        Assert.DoesNotContain(colleague.Id, assignees);
        Assert.Contains(outside.Id, assignees);
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/api/leads/{leads[colleague.Id]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/api/projects/{projects[colleague.Id]}")).StatusCode);
    }
}
