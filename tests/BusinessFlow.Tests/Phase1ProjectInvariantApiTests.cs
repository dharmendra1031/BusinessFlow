using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1ProjectInvariantApiTests
{
    private static async Task<(long ProjectId, long OwnerId)> CreateProjectAsync(
        Phase1TestDatabase database, HttpClient client, string status = "InProgress")
    {
        var ownerId = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        await using var db = database.CreateContext();
        var lead = new Lead
        {
            Name = "Delivery customer", Phone = "9876543210", SourceId = 1,
            StatusId = 5, AssignedToId = ownerId, CreatedById = ownerId
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        var project = new DevelopmentProject
        {
            LeadId = lead.Id, Title = "Delivery", OwnerId = ownerId,
            CreatedById = ownerId, Status = status
        };
        db.DevelopmentProjects.Add(project);
        await db.SaveChangesAsync();
        return (project.Id, ownerId);
    }

    [Fact]
    public async Task Completed_project_rejects_new_bugs_and_changes_to_existing_bugs()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        var (projectId, ownerId) = await CreateProjectAsync(database, admin, "Completed");
        long bugId;
        await using (var db = database.CreateContext())
        {
            var bug = new ProjectBug
            {
                ProjectId = projectId, Title = "Historical issue", Status = "Closed",
                ReportedById = ownerId, ClosedAtUtc = DateTime.UtcNow
            };
            db.ProjectBugs.Add(bug);
            await db.SaveChangesAsync();
            bugId = bug.Id;
        }

        var path = $"/api/projects/{projectId}/bugs";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path,
            new { title = "New issue", severity = "High" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path + "/" + bugId,
            new { title = "Historical issue", severity = "High", status = "Open" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync(path + "/" + bugId + "/status",
            new { status = "Open" })).StatusCode);

        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.ProjectBugs.CountAsync(b => b.ProjectId == projectId));
        Assert.Equal("Closed", (await verify.ProjectBugs.SingleAsync(b => b.Id == bugId)).Status);
    }

    [Fact]
    public async Task Completed_milestones_and_sprints_prevent_task_status_regression()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        var (projectId, _) = await CreateProjectAsync(database, admin);
        await using (var db = database.CreateContext())
        {
            var milestone = new ProjectMilestone
            {
                ProjectId = projectId, Title = "Milestone", Status = "Completed"
            };
            var sprint = new ProjectSprint
            {
                ProjectId = projectId, Title = "Sprint", Status = "Completed",
                StartDate = new DateOnly(2026, 11, 1), DueDate = new DateOnly(2026, 11, 30)
            };
            db.ProjectMilestones.Add(milestone);
            db.ProjectSprints.Add(sprint);
            await db.SaveChangesAsync();
            db.DevelopmentTasks.AddRange(
                new DevelopmentTask
                {
                    ProjectId = projectId, Title = "Milestone task",
                    MilestoneId = milestone.Id, Status = "Done"
                },
                new DevelopmentTask
                {
                    ProjectId = projectId, Title = "Sprint task",
                    SprintId = sprint.Id, Status = "Done"
                });
            await db.SaveChangesAsync();
        }

        var path = $"/api/projects/{projectId}";
        var project = await admin.GetFromJsonAsync<JsonElement>(path);
        foreach (var task in project.GetProperty("tasks").EnumerateArray())
        {
            var id = task.GetProperty("id").GetInt64();
            var version = task.GetProperty("rowVersion").GetString();
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PatchAsJsonAsync(path + "/tasks/" + id + "/status",
                    new { status = "Todo", rowVersion = version })).StatusCode);
        }

        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.DevelopmentTasks.CountAsync(t =>
            t.ProjectId == projectId && t.Status == "Done"));
    }

    [Fact]
    public async Task Planning_deadlines_cannot_precede_linked_task_deadlines()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        var (projectId, _) = await CreateProjectAsync(database, admin);
        long milestoneId;
        long sprintId;
        await using (var db = database.CreateContext())
        {
            var milestone = new ProjectMilestone
            {
                ProjectId = projectId, Title = "Milestone",
                DueDate = new DateOnly(2026, 11, 30)
            };
            var sprint = new ProjectSprint
            {
                ProjectId = projectId, Title = "Sprint",
                StartDate = new DateOnly(2026, 11, 1), DueDate = new DateOnly(2026, 11, 30)
            };
            db.ProjectMilestones.Add(milestone);
            db.ProjectSprints.Add(sprint);
            await db.SaveChangesAsync();
            milestoneId = milestone.Id;
            sprintId = sprint.Id;
            db.DevelopmentTasks.Add(new DevelopmentTask
            {
                ProjectId = projectId, Title = "Future task",
                MilestoneId = milestoneId, SprintId = sprintId,
                DueDate = new DateOnly(2026, 11, 25)
            });
            await db.SaveChangesAsync();
        }

        var path = $"/api/projects/{projectId}/planning";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(
            path + "/milestones/" + milestoneId,
            new { title = "Milestone", status = "Planned", dueDate = "2026-11-20" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(
            path + "/sprints/" + sprintId,
            new { title = "Sprint", status = "Planned", startDate = "2026-11-01", dueDate = "2026-11-20" })).StatusCode);

        await using var verify = database.CreateContext();
        Assert.Equal(new DateOnly(2026, 11, 30),
            (await verify.ProjectMilestones.SingleAsync(x => x.Id == milestoneId)).DueDate);
        Assert.Equal(new DateOnly(2026, 11, 30),
            (await verify.ProjectSprints.SingleAsync(x => x.Id == sprintId)).DueDate);
    }
}
