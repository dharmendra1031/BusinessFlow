using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1AuditAtomicityTests
{
    private static async Task BlockCreateAuditsAsync(Phase1TestDatabase database)
    {
        await using var db = database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER dbo.TestBlockCreationAudits ON dbo.AuditLogs
            AFTER INSERT
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted
                    WHERE Action = N'Create' AND TableName IN
                        (N'DevelopmentProjects', N'DevelopmentTasks',
                         N'ProjectMilestones', N'ProjectSprints'))
                    THROW 50001, 'Test audit insert rejected', 1;
            END
            """);
    }

    private static async Task ExpectAuditFailureAsync(Task<HttpResponseMessage> action)
    {
        try
        {
            using var response = await action;
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 50001 })
        {
            // An unhandled test-server exception is also acceptable.
        }
    }

    private static async Task<(long UserId, long LeadId)> CreateWonLeadAsync(
        Phase1TestDatabase database, HttpClient client)
    {
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        await using var db = database.CreateContext();
        var lead = new Lead
        {
            Name = "Audit rollback customer", Phone = "9876543210",
            StatusId = 5, SourceId = 1, AssignedToId = userId, CreatedById = userId
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return (userId, lead.Id);
    }

    [Fact]
    public async Task Failed_project_audit_does_not_leave_created_project()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        var (userId, leadId) = await CreateWonLeadAsync(database, admin);
        await BlockCreateAuditsAsync(database);

        await ExpectAuditFailureAsync(admin.PostAsJsonAsync("/api/projects",
            new { leadId, title = "Rollback project", ownerId = userId }));

        await using var verify = database.CreateContext();
        Assert.Equal(0, await verify.DevelopmentProjects.CountAsync());
        Assert.Equal(0, await verify.AuditLogs.CountAsync(x =>
            x.TableName == "DevelopmentProjects"));
    }

    [Fact]
    public async Task Failed_task_milestone_and_sprint_audits_leave_no_partial_records()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        var (userId, leadId) = await CreateWonLeadAsync(database, admin);
        long projectId;
        await using (var db = database.CreateContext())
        {
            var project = new DevelopmentProject
            {
                LeadId = leadId, Title = "Existing project", OwnerId = userId,
                CreatedById = userId, Status = "InProgress"
            };
            db.DevelopmentProjects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;
        }
        await BlockCreateAuditsAsync(database);

        await ExpectAuditFailureAsync(admin.PostAsJsonAsync(
            $"/api/projects/{projectId}/tasks",
            new { title = "Rollback task", priority = "Medium" }));
        await ExpectAuditFailureAsync(admin.PostAsJsonAsync(
            $"/api/projects/{projectId}/planning/milestones",
            new { title = "Rollback milestone", status = "Planned" }));
        await ExpectAuditFailureAsync(admin.PostAsJsonAsync(
            $"/api/projects/{projectId}/planning/sprints",
            new { title = "Rollback sprint", status = "Planned",
                startDate = "2026-11-01", dueDate = "2026-11-30" }));

        await using var verify = database.CreateContext();
        Assert.Equal(0, await verify.DevelopmentTasks.CountAsync());
        Assert.Equal(0, await verify.ProjectMilestones.CountAsync());
        Assert.Equal(0, await verify.ProjectSprints.CountAsync());
        Assert.Equal(0, await verify.AuditLogs.CountAsync(x =>
            x.TableName == "DevelopmentTasks" ||
            x.TableName == "ProjectMilestones" || x.TableName == "ProjectSprints"));
    }
}
