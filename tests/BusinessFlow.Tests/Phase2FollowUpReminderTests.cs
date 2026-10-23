using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2FollowUpReminderTests
{
    [Fact]
    public async Task Reminder_inbox_is_scoped_to_current_lead_access_and_owner_transfer()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var first = await host.ClientAsync("Employee");
        using var second = await host.ClientAsync("Employee");

        static async Task<long> UserId(HttpClient client) =>
            (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var adminId = await UserId(admin);
        var firstId = await UserId(first);
        var secondId = await UserId(second);

        long firstLeadId;
        long secondLeadId;
        long firstReminderId;
        await using (var db = database.CreateContext())
        {
            var firstLead = new Lead
            {
                Name = "First team's customer", Phone = "9876543210", SourceId = 1,
                AssignedToId = firstId, CreatedById = adminId
            };
            var secondLead = new Lead
            {
                Name = "Second team's customer", Phone = "9876543211", SourceId = 1,
                AssignedToId = secondId, CreatedById = adminId
            };
            db.Leads.AddRange(firstLead, secondLead);
            await db.SaveChangesAsync();
            firstLeadId = firstLead.Id;
            secondLeadId = secondLead.Id;

            var now = DateTime.UtcNow;
            var firstSoon = new LeadFollowUp
            {
                LeadId = firstLeadId, AssignedToId = firstId,
                CreatedById = adminId, DueAtUtc = now.AddHours(2), Notes = "Call primary contact"
            };
            db.LeadFollowUps.AddRange(
                firstSoon,
                new LeadFollowUp
                {
                    LeadId = firstLeadId, AssignedToId = firstId, CreatedById = adminId,
                    DueAtUtc = now.AddHours(-2), Notes = "Overdue call"
                },
                new LeadFollowUp
                {
                    LeadId = secondLeadId, AssignedToId = secondId, CreatedById = adminId,
                    DueAtUtc = now.AddHours(10), Notes = "Second team's call"
                },
                new LeadFollowUp
                {
                    LeadId = secondLeadId, AssignedToId = secondId, CreatedById = adminId,
                    DueAtUtc = now.AddHours(30), Notes = "Outside reminder window"
                },
                new LeadFollowUp
                {
                    LeadId = firstLeadId, AssignedToId = firstId, CreatedById = adminId,
                    DueAtUtc = now.AddHours(3), CompletedAtUtc = now.AddMinutes(-5),
                    Notes = "Already finished"
                });
            await db.SaveChangesAsync();
            firstReminderId = firstSoon.Id;
        }

        static async Task<JsonElement> Get(HttpClient client, string query) =>
            await client.GetFromJsonAsync<JsonElement>("/api/followups?" + query);

        Assert.Equal(2, (await Get(admin, "view=dueSoon")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Get(admin, "view=dueSoon&mine=true")).GetProperty("total").GetInt32());

        var firstSoonItems = await Get(first, "view=dueSoon&mine=true");
        Assert.Equal(1, firstSoonItems.GetProperty("total").GetInt32());
        Assert.Equal(firstReminderId, firstSoonItems.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(firstId, firstSoonItems.GetProperty("items")[0].GetProperty("assignedToId").GetInt64());
        Assert.Equal(1, (await Get(first, "view=overdue&mine=true")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Get(first, "view=completed&mine=true")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Get(second, "view=dueSoon&mine=true")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Get(second, "view=overdue&mine=true")).GetProperty("total").GetInt32());

        var invalid = await admin.GetAsync("/api/followups?view=tomorrow&mine=true");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var version = (await admin.GetFromJsonAsync<JsonElement>($"/api/leads/{firstLeadId}"))
            .GetProperty("rowVersion").GetString();
        var transfer = await admin.PostAsJsonAsync($"/api/leads/{firstLeadId}/assign",
            new { userId = secondId, rowVersion = version });
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);
        Assert.Equal(0, (await Get(first, "view=dueSoon&mine=true")).GetProperty("total").GetInt32());
        Assert.Equal(2, (await Get(second, "view=dueSoon&mine=true")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Get(second, "view=overdue&mine=true")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound,
            (await first.PatchAsync($"/api/followups/{firstReminderId}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await second.PatchAsync($"/api/followups/{firstReminderId}/complete", null)).StatusCode);

        Assert.Equal(1, (await Get(second, "view=dueSoon&mine=true")).GetProperty("total").GetInt32());
        await using var checkedDb = database.CreateContext();
        Assert.Equal(secondId, await checkedDb.LeadFollowUps.Where(x => x.Id == firstReminderId)
            .Select(x => x.AssignedToId).SingleAsync());
        Assert.True(await checkedDb.LeadFollowUps.Where(x => x.Id == firstReminderId)
            .AnyAsync(x => x.CompletedAtUtc != null));
    }
}
