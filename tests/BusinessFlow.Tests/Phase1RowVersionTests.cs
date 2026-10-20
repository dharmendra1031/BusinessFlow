using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1RowVersionTests
{
    [Fact]
    public async Task Sql_server_rejects_stale_lead_project_and_task_updates()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var first = fixture.CreateContext();
        var person = new ApplicationUser { FullName = "Owner" };
        first.Users.Add(person);
        await first.SaveChangesAsync();
        var lead = new Lead { Name = "Customer", Phone = "1234567890", SourceId = 1, StatusId = 5,
            CreatedById = person.Id, AssignedToId = person.Id };
        first.Leads.Add(lead);
        await first.SaveChangesAsync();
        var project = new DevelopmentProject { LeadId = lead.Id, OwnerId = person.Id, CreatedById = person.Id, Title = "Project" };
        first.DevelopmentProjects.Add(project);
        await first.SaveChangesAsync();
        var task = new DevelopmentTask { ProjectId = project.Id, Title = "Task" };
        first.DevelopmentTasks.Add(task);
        await first.SaveChangesAsync();
        await using var second = fixture.CreateContext();
        var staleLead = await second.Leads.SingleAsync();
        var staleProject = await second.DevelopmentProjects.SingleAsync();
        var staleTask = await second.DevelopmentTasks.SingleAsync();
        lead.Name = "New customer";
        project.Title = "New project";
        task.Title = "New task";
        await first.SaveChangesAsync();
        staleLead.Name = "Stale customer";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        second.Entry(staleLead).State = EntityState.Unchanged;
        staleProject.Title = "Stale project";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        second.Entry(staleProject).State = EntityState.Unchanged;
        staleTask.Title = "Stale task";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = fixture.CreateContext();
        Assert.Equal("New customer", (await verify.Leads.SingleAsync()).Name);
        Assert.Equal("New project", (await verify.DevelopmentProjects.SingleAsync()).Title);
        Assert.Equal("New task", (await verify.DevelopmentTasks.SingleAsync()).Title);
    }
}
