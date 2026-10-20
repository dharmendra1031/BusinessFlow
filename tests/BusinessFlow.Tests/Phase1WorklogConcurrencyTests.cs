using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1WorklogConcurrencyTests
{
    [Fact]
    public async Task Parallel_posts_across_projects_preserve_the_daily_limit_and_audit_atomicity()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var client = await host.ClientAsync("Employee");
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        await using var db = database.CreateContext();
        var leads = Enumerable.Range(0, 2).Select(index => new Lead { Name = "Customer " + index,
            Phone = "1234567890", StatusId = 5, SourceId = 1, CreatedById = userId, AssignedToId = userId }).ToArray();
        db.Leads.AddRange(leads);
        await db.SaveChangesAsync();
        var projects = leads.Select(lead => new DevelopmentProject { LeadId = lead.Id, Title = "Delivery " + lead.Id,
            OwnerId = userId, CreatedById = userId }).ToArray();
        db.DevelopmentProjects.AddRange(projects);
        await db.SaveChangesAsync();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        // Concurrent HTTP requests create independent scoped contexts/connections.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => client.PostAsJsonAsync(
            $"/api/projects/{projects[index % 2].Id}/worklogs", new { workDate = date, hours = 8, description = "Parallel work " + index })));
        Assert.All(responses, response => Assert.Contains(response.StatusCode,
            new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        var accepted = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
        Assert.InRange(accepted, 1, 3);
        await using var verify = database.CreateContext();
        var logs = await verify.DeveloperWorklogs.Where(x => x.UserId == userId && x.WorkDate == date).ToListAsync();
        Assert.Equal(accepted, logs.Count);
        Assert.Equal(8m * accepted, logs.Sum(x => x.Hours));
        Assert.True(logs.Sum(x => x.Hours) <= 24);
        var audits = await verify.AuditLogs.Where(x => x.TableName == "DeveloperWorklogs").ToListAsync();
        Assert.Equal(accepted, audits.Count);
        Assert.All(audits, audit => Assert.Contains(audit.RecordId, logs.Select(log => log.Id)));
    }
}
