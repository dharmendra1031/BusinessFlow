using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1EmployeeOwnershipTests
{
    [Theory]
    [InlineData("tasks")]
    [InlineData("bugs")]
    [InlineData("worklogs")]
    public async Task Employee_project_owner_does_not_manage_colleagues_work(string area)
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var employee = await host.ClientAsync("Employee");
        var id = (await employee.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        await using var db = database.CreateContext();
        var colleague = new ApplicationUser { FullName = "Colleague" };
        db.Users.Add(colleague);
        var lead = new Lead { Name = "Customer", Phone = "1234567890", SourceId = 1, StatusId = 5, AssignedToId = id, CreatedById = id };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        var project = new DevelopmentProject { LeadId = lead.Id, Title = "Delivery", OwnerId = id, CreatedById = id };
        db.DevelopmentProjects.Add(project);
        await db.SaveChangesAsync();
        var task = new DevelopmentTask { ProjectId = project.Id, Title = "Colleague task", AssignedToId = colleague.Id };
        var bug = new ProjectBug { ProjectId = project.Id, Title = "Colleague bug", ReportedById = id, AssignedToId = colleague.Id };
        db.DevelopmentTasks.Add(task);
        db.ProjectBugs.Add(bug);
        db.DeveloperWorklogs.AddRange(
            new DeveloperWorklog { ProjectId = project.Id, UserId = id, WorkDate = DateOnly.FromDateTime(DateTime.UtcNow), Hours = 1, Description = "Own work" },
            new DeveloperWorklog { ProjectId = project.Id, UserId = colleague.Id, WorkDate = DateOnly.FromDateTime(DateTime.UtcNow), Hours = 2, Description = "Colleague work" });
        await db.SaveChangesAsync();
        if (area == "worklogs")
        {
            var result = await employee.GetFromJsonAsync<JsonElement>($"/api/projects/{project.Id}/worklogs");
            Assert.Equal(1, result.GetProperty("total").GetInt32());
            Assert.Equal(1m, result.GetProperty("totalHours").GetDecimal());
        }
        else
        {
            var itemId = area == "tasks" ? task.Id : bug.Id;
            var response = await employee.PatchAsJsonAsync($"/api/projects/{project.Id}/{area}/{itemId}/status", new { status = "InProgress" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
