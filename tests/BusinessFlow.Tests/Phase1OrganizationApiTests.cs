using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1OrganizationApiTests
{
    [Fact]
    public async Task Only_admin_can_manage_organization_and_invalid_names_are_rejected()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/organization")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/organization")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(
            "/api/admin/organization/departments", new { name = "x", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(
            "/api/admin/organization/departments", new { name = "Engineering", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync(
            "/api/admin/organization/departments", new { name = "Unauthorized", isActive = true })).StatusCode);
    }

    [Fact]
    public async Task Admin_can_transfer_membership_and_cannot_disable_populated_teams()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        var department = await admin.PostAsJsonAsync("/api/admin/organization/departments", new { name = "Delivery", isActive = true });
        var departmentId = (await department.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var team = await admin.PostAsJsonAsync("/api/admin/organization/teams", new { name = "Platform", departmentId, isActive = true });
        var teamId = (await team.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await using var db = database.CreateContext();
        var userId = await db.Users.Where(x => x.FullName == "Employee").Select(x => x.Id).SingleAsync();
        var path = $"/api/admin/organization/employees/{userId}/membership";
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync(path, new { departmentId, teamIds = new[] { teamId } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path, new { departmentId, teamIds = new[] { teamId } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/organization/teams/{teamId}",
            new { name = "Platform", departmentId, isActive = false })).StatusCode);
        Assert.Equal(userId, (await db.TeamMemberships.SingleAsync()).UserId);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path, new { departmentId = (long?)null, teamIds = Array.Empty<long>() })).StatusCode);
        Assert.Empty(await db.TeamMemberships.AsNoTracking().ToListAsync());
    }
}
