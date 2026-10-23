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
    public async Task Reminders_respect_owner_due_window_and_lead_scope_after_reassignment()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        using var colleague = await host.ClientAsync("Employee");

        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var employeeId = (await employee.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var colleagueId = (await colleague.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var now = DateTime.UtcNow;
        long leadId;
        await using (var db = database.CreateContext())
        {
            var lead = new Lead
            {
                Name = "Reminders employee lead", Phone = "9876543210",
                SourceId = 1, StatusId = 1, CreatedById = adminId, AssignedToId = employeeId
            };
            var privateLead = new Lead
            {
                Name = "Colleague private lead", Phone = "9876543211",
                SourceId = 1, StatusId = 1, CreatedById = adminId, AssignedToId = colleagueId
            };
            db.Leads.AddRange(lead, privateLead);
            await db.SaveChangesAsync();
            leadId = lead.Id;
            db.LeadFollowUps.AddRange(
                new LeadFollowUp { LeadId = lead.Id, AssignedToId = employeeId,
                    CreatedById = adminId, Notes = "Call overdue", DueAtUtc = now.AddHours(-2) },
                new LeadFollowUp { LeadId = lead.Id, AssignedToId = employeeId,
                    CreatedById = adminId, Notes = "Call soon", DueAtUtc = now.AddHours(8) },
                new LeadFollowUp { LeadId = lead.Id, AssignedToId = employeeId,
                    CreatedById = adminId, Notes = "Call later", DueAtUtc = now.AddDays(5) },
                new LeadFollowUp { LeadId = lead.Id, AssignedToId = employeeId,
                    CreatedById = adminId, Notes = "Completed call", DueAtUtc = now.AddHours(-1),
                    CompletedAtUtc = now.AddMinutes(-30) },
                new LeadFollowUp { LeadId = privateLead.Id, AssignedToId = colleagueId,
                    CreatedById = adminId, Notes = "Private colleague reminder", DueAtUtc = now.AddHours(-1) }
            );
            await db.SaveChangesAsync();
        }

        var reminders = await employee.GetFromJsonAsync<JsonElement>(
            "/api/followups/reminders?withinHours=24&pageSize=10");
        Assert.Equal(2, reminders.GetProperty("total").GetInt32());
        Assert.Equal(1, reminders.GetProperty("overdue").GetInt32());
        Assert.Equal(1, reminders.GetProperty("upcoming").GetInt32());
        var items = reminders.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, x => Assert.Equal(employeeId, x.GetProperty("assignedToId").GetInt64()));
        Assert.Equal("Call overdue", items[0].GetProperty("notes").GetString());
        Assert.DoesNotContain(items, x => x.GetProperty("notes").GetString() == "Private colleague reminder");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await employee.GetAsync("/api/followups/reminders?withinHours=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await employee.GetAsync("/api/followups/reminders?withinHours=73")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await employee.GetAsync("/api/followups/reminders?pageSize=51")).StatusCode);

        var lead = await admin.GetFromJsonAsync<JsonElement>("/api/leads/" + leadId);
        var assigned = await admin.PostAsJsonAsync("/api/leads/" + leadId + "/assign",
            new { userId = colleagueId, rowVersion = lead.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        var employeeAfter = await employee.GetFromJsonAsync<JsonElement>(
            "/api/followups/reminders?withinHours=24");
        Assert.Equal(0, employeeAfter.GetProperty("total").GetInt32());

        var colleagueAfter = await colleague.GetFromJsonAsync<JsonElement>(
            "/api/followups/reminders?withinHours=24");
        Assert.Equal(3, colleagueAfter.GetProperty("total").GetInt32());
        Assert.Equal(2, colleagueAfter.GetProperty("overdue").GetInt32());
        await using var check = database.CreateContext();
        Assert.Equal(2, await check.LeadFollowUps.CountAsync(x =>
            x.LeadId == leadId && x.CompletedAtUtc == null && x.AssignedToId == colleagueId &&
            x.DueAtUtc <= now.AddDays(1)));
        Assert.Equal(1, await check.LeadFollowUps.CountAsync(x =>
            x.LeadId == leadId && x.CompletedAtUtc != null && x.AssignedToId == employeeId));
    }

    [Fact]
    public async Task Anonymous_requests_cannot_read_employee_reminders()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/followups/reminders")).StatusCode);
    }
}
