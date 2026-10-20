using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1DirectoryTests
{
    [Fact]
    public async Task Directory_has_stable_pages_totals_and_combined_filters()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        await using var db = database.CreateContext();
        var department = new Department { Name = "Engineering" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var team = new Team { Name = "Platform", DepartmentId = department.Id };
        db.Teams.Add(team);
        var people = Enumerable.Range(0, 205).Select(index => new ApplicationUser { FullName = "Directory employee",
            Email = $"directory{index}@test.local", DepartmentId = index % 2 == 0 ? department.Id : null,
            IsActive = index < 200 }).ToArray();
        db.Users.AddRange(people);
        await db.SaveChangesAsync();
        db.TeamMemberships.Add(new TeamMembership { TeamId = team.Id, UserId = people[0].Id });
        await db.SaveChangesAsync();
        var first = await admin.GetFromJsonAsync<JsonElement>("/api/employees?search=Directory&page=1&pageSize=20");
        var second = await admin.GetFromJsonAsync<JsonElement>("/api/employees?search=Directory&page=2&pageSize=20");
        Assert.Equal(205, first.GetProperty("total").GetInt32());
        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        var firstIds = first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
        var secondIds = second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.True(firstIds.Max() < secondIds.Min());
        var filtered = await admin.GetFromJsonAsync<JsonElement>($"/api/employees?search=directory0%40&departmentId={department.Id}&teamId={team.Id}&isActive=true");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var lookup = await admin.GetFromJsonAsync<JsonElement>("/api/employees/lookup?action=leads.assign&search=directory204%40");
        Assert.Equal(0, lookup.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/employees?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/employees/lookup?action=unknown.action")).StatusCode);
    }
}
