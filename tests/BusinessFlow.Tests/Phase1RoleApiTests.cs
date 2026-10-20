using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1RoleApiTests
{
    [Fact]
    public async Task Admin_manages_custom_grants_memberships_and_deny_only_restrictions()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/roles")).StatusCode);
        var create = await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Quality" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var roleId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var grantPath = $"/api/admin/roles/{roleId}/grants/leads.assign";
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(grantPath, new { scope = "Department" })).StatusCode);
        await using var db = database.CreateContext();
        var userId = await db.Users.Where(x => x.FullName == "Employee").Select(x => x.Id).SingleAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/roles/{roleId}/members/{userId}", new { assigned = true })).StatusCode);
        using var refreshed = await host.ClientForUserAsync(userId);
        var effective = await refreshed.GetFromJsonAsync<JsonElement>("/api/auth/permissions");
        Assert.True(effective.GetProperty("permissions").GetProperty("leads.assign").GetBoolean());
        var restrictionPath = $"/api/admin/roles/employees/{userId}/restrictions/leads.assign";
        Assert.Equal(HttpStatusCode.Unauthorized, (await employee.PutAsJsonAsync(restrictionPath, new { denied = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await refreshed.PutAsJsonAsync(restrictionPath, new { denied = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(restrictionPath, new { denied = true })).StatusCode);
        effective = await refreshed.GetFromJsonAsync<JsonElement>("/api/auth/permissions");
        Assert.False(effective.GetProperty("permissions").GetProperty("leads.assign").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/roles/{roleId}/grants/roles.manage", new { scope = "Organization" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/admin/roles/{roleId}")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_deactivation_cannot_remove_every_active_administrator()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var first = await host.ClientAsync("Admin");
        using var second = await host.ClientAsync("Admin");
        await using var db = database.CreateContext();
        var ids = await db.Users.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var responses = await Task.WhenAll(
            first.PatchAsJsonAsync($"/api/admin/users/{ids[1]}/status", false),
            second.PatchAsJsonAsync($"/api/admin/users/{ids[0]}/status", false));
        Assert.True(await db.Users.AsNoTracking().CountAsync(x => x.IsActive) >= 1);
        Assert.True(responses.Count(x => x.IsSuccessStatusCode) <= 1);
        Assert.All(responses, response => Assert.Contains(response.StatusCode,
            new[] { HttpStatusCode.NoContent, HttpStatusCode.BadRequest, HttpStatusCode.Conflict, HttpStatusCode.Unauthorized }));
    }
}
