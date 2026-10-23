using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2FollowUpReminderTests
{
    private static async Task<long> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();

    [Fact]
    public async Task Reminders_are_due_windowed_and_respect_assignee_and_lead_scope()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var first = await host.ClientAsync("Employee");
        using var second = await host.ClientAsync("Employee");
        var adminId = await UserIdAsync(admin);
        var firstId = await UserIdAsync(first);
        var secondId = await UserIdAsync(second);
        long firstLeadId, secondLeadId, foreignFollowUpId;
        var now = DateTime.UtcNow;

        await using (var db = database.CreateContext())
        {
            var firstLead = new Lead
            {
                Name = "First employee customer", Phone = "9876543210",
                CreatedById = adminId, AssignedToId = firstId, SourceId = 1, StatusId = 1
            };
            var secondLead = new Lead
            {
                Name = "Second employee customer", Phone = "9876543211",
                CreatedById = adminId, AssignedToId = secondId, SourceId = 1, StatusId = 1
            };
            db.Leads.AddRange(firstLead, secondLead);
            await db.SaveChangesAsync();
            firstLeadId = firstLead.Id;
            secondLeadId = secondLead.Id;
            var foreign = new LeadFollowUp
            {
                LeadId = secondLeadId, AssignedToId = secondId, CreatedById = adminId,
                DueAtUtc = now.AddHours(3), Notes = "Another employee's reminder"
            };
            db.LeadFollowUps.AddRange(
                new LeadFollowUp { LeadId = firstLeadId, AssignedToId = firstId,
                    CreatedById = adminId, DueAtUtc = now.AddHours(-2), Notes = "Overdue call" },
                new LeadFollowUp { LeadId = firstLeadId, AssignedToId = firstId,
                    CreatedById = adminId, DueAtUtc = now.AddHours(2), Notes = "Call today" },
                new LeadFollowUp { LeadId = firstLeadId, AssignedToId = firstId,
                    CreatedById = adminId, DueAtUtc = now.AddHours(48), Notes = "Call later" },
                new LeadFollowUp { LeadId = firstLeadId, AssignedToId = firstId,
                    CreatedById = adminId, DueAtUtc = now.AddHours(-4),
                    CompletedAtUtc = now.AddHours(-3), Notes = "Already completed" },
                foreign);
            await db.SaveChangesAsync();
            foreignFollowUpId = foreign.Id;
        }

        var mine = await first.GetFromJsonAsync<JsonElement>("/api/followups/reminder-summary");
        Assert.Equal(1, mine.GetProperty("overdue").GetInt32());
        Assert.Equal(1, mine.GetProperty("dueSoon").GetInt32());
        Assert.Equal(1, mine.GetProperty("later").GetInt32());
        Assert.Equal(3, mine.GetProperty("pendingTotal").GetInt32());

        var due = await first.GetFromJsonAsync<JsonElement>("/api/followups?view=dueSoon&mineOnly=true");
        Assert.Equal(1, due.GetProperty("total").GetInt32());
        Assert.Equal(firstId, due.GetProperty("items")[0].GetProperty("assignedToId").GetInt64());

        var overdue = await first.GetFromJsonAsync<JsonElement>("/api/followups?view=overdue&mineOnly=true");
        Assert.Equal(1, overdue.GetProperty("total").GetInt32());
        var adminAll = await admin.GetFromJsonAsync<JsonElement>("/api/followups/reminder-summary?mineOnly=false");
        Assert.Equal(5 - 1, adminAll.GetProperty("pendingTotal").GetInt32());
        Assert.Equal(2, adminAll.GetProperty("dueSoon").GetInt32());
        var adminMine = await admin.GetFromJsonAsync<JsonElement>("/api/followups/reminder-summary?mineOnly=true");
        Assert.Equal(0, adminMine.GetProperty("pendingTotal").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound,
            (await first.PatchAsync($"/api/followups/{foreignFollowUpId}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await first.GetAsync($"/api/leads/{secondLeadId}/followups")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await first.GetAsync("/api/followups?view=invalid&mineOnly=true")).StatusCode);

        // Existing assignment workflow transfers only pending follow-ups.
        var lead = await admin.GetFromJsonAsync<JsonElement>($"/api/leads/{firstLeadId}");
        var version = lead.GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"/api/leads/{firstLeadId}/assign",
                new { userId = secondId, rowVersion = version })).StatusCode);
        mine = await first.GetFromJsonAsync<JsonElement>("/api/followups/reminder-summary");
        Assert.Equal(0, mine.GetProperty("pendingTotal").GetInt32());
        var nowSecond = await second.GetFromJsonAsync<JsonElement>("/api/followups/reminder-summary");
        Assert.Equal(4, nowSecond.GetProperty("pendingTotal").GetInt32());

        await using var check = database.CreateContext();
        Assert.Equal(3, await check.LeadFollowUps.CountAsync(x =>
            x.LeadId == firstLeadId && x.CompletedAtUtc == null && x.AssignedToId == secondId));
        Assert.Equal(1, await check.LeadFollowUps.CountAsync(x =>
            x.LeadId == firstLeadId && x.CompletedAtUtc != null && x.AssignedToId == firstId));
    }

    [Fact]
    public async Task Restricted_role_cannot_query_reminder_counts()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        var id = await UserIdAsync(employee);
        var response = await admin.PutAsJsonAsync(
            $"/api/admin/roles/employees/{id}/restrictions/followups.view",
            new { denied = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.GetAsync("/api/followups/reminder-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.GetAsync("/api/followups?view=dueSoon&mineOnly=true")).StatusCode);
    }
}
