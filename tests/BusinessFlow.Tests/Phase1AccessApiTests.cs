using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1AccessApiTests
{
    [Fact]
    public async Task Wider_lead_read_grant_does_not_leak_into_edit_reports_or_dashboard()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var employee = await host.ClientAsync("Employee");
        using var other = await host.ClientAsync("Employee");
        var me = (await employee.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var outside = (await other.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        await using var db = database.CreateContext();
        var own = new Lead { Name = "Own lead", Phone = "1234567890", SourceId = 1, StatusId = 1, CreatedById = me, AssignedToId = me };
        var foreign = new Lead { Name = "Outside lead", Phone = "1234567891", SourceId = 1, StatusId = 1, CreatedById = outside, AssignedToId = outside };
        db.Leads.AddRange(own, foreign);
        await db.SaveChangesAsync();
        var administration = new RoleAdministrationService(db);
        var role = await administration.CreateRoleAsync("Audit reader", me);
        await administration.SetGrantAsync(role, "leads.view", DataScope.Organization, me);
        await administration.SetMemberAsync(role, me, true, outside);
        using var refreshed = await host.ClientForUserAsync(me);
        Assert.Equal(2, (await refreshed.GetFromJsonAsync<JsonElement>("/api/leads")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await refreshed.GetAsync($"/api/leads/{foreign.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await refreshed.PutAsJsonAsync($"/api/leads/{foreign.Id}",
            new { name = "Unauthorized edit", phone = "1234567891", sourceId = 1 })).StatusCode);
        Assert.Equal(1, (await refreshed.GetFromJsonAsync<JsonElement>("/api/reports")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await refreshed.GetFromJsonAsync<JsonElement>("/api/dashboard")).GetProperty("totalLeads").GetInt32());
        var csv = await refreshed.GetStringAsync("/api/reports/export");
        Assert.Contains("Own lead", csv);
        Assert.DoesNotContain("Outside lead", csv);
        await administration.SetRestrictionAsync(me, "leads.view", true, outside);
        Assert.Equal(HttpStatusCode.Forbidden, (await refreshed.GetAsync($"/api/leads/{foreign.Id}/history")).StatusCode);
        Assert.Equal("Outside lead", (await db.Leads.AsNoTracking().SingleAsync(x => x.Id == foreign.Id)).Name);
    }
}
