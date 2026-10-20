using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1BugPermissionTests
{
    [Fact]
    public async Task Status_or_edit_grant_cannot_bypass_the_separate_bug_close_permission()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var employee = await host.ClientAsync("Employee");
        var id = (await employee.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        await using var db = database.CreateContext();
        var lead = new Lead { Name = "Customer", Phone = "1234567890", SourceId = 1, StatusId = 5, AssignedToId = id, CreatedById = id };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        var project = new DevelopmentProject { LeadId = lead.Id, Title = "Delivery", OwnerId = id, CreatedById = id };
        db.DevelopmentProjects.Add(project);
        await db.SaveChangesAsync();
        var bug = new ProjectBug { ProjectId = project.Id, Title = "Failure", ReportedById = id, AssignedToId = id };
        db.ProjectBugs.Add(bug);
        await db.SaveChangesAsync();
        var administration = new RoleAdministrationService(db);
        var role = await administration.CreateRoleAsync("Bug editor", id);
        await administration.SetGrantAsync(role, "bugs.edit", DataScope.Self, id);
        await administration.SetMemberAsync(role, id, true, id);
        using var refreshed = await host.ClientForUserAsync(id);
        var path = $"/api/projects/{project.Id}/bugs/{bug.Id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await refreshed.PatchAsJsonAsync(path + "/status", new { status = "Closed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await refreshed.PutAsJsonAsync(path,
            new { title = "Failure", severity = "Medium", status = "Closed", assignedToId = id })).StatusCode);
        await administration.SetRestrictionAsync(id, "bugs.status", true, id);
        Assert.Equal(HttpStatusCode.Forbidden, (await refreshed.PutAsJsonAsync(path,
            new { title = "Failure", severity = "Medium", status = "InProgress", assignedToId = id })).StatusCode);
    }
}
