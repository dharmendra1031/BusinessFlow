using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2LeadSourceTests
{
    [Fact]
    public async Task Only_admin_manages_sources_and_deactivation_preserves_historical_leads()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.GetAsync("/api/admin/lead-sources")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.PostAsJsonAsync("/api/admin/lead-sources",
                new { name = "Unauthorized", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/admin/lead-sources",
                new { name = "X", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/admin/lead-sources",
                new { name = "manual", isActive = true })).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/admin/lead-sources",
            new { name = "  Partner Event  ", isActive = true });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var sourceId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();

        var lead = await admin.PostAsJsonAsync("/api/leads",
            new { name = "Historical source customer", phone = "1234567890", sourceId });
        Assert.Equal(HttpStatusCode.Created, lead.StatusCode);
        var leadId = (await lead.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var deactivate = await admin.PutAsJsonAsync("/api/admin/lead-sources/" + sourceId,
            new { name = "Partner Referral", isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var activeSources = await admin.GetFromJsonAsync<JsonElement>("/api/masters/lead-sources");
        Assert.DoesNotContain(activeSources.EnumerateArray(),
            item => item.GetProperty("id").GetInt64() == sourceId);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/leads?pageSize=100");
        var existing = history.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt64() == leadId);
        Assert.Equal("Partner Referral", existing.GetProperty("source").GetString());
        Assert.Equal(sourceId, existing.GetProperty("sourceId").GetInt64());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/leads",
                new { name = "New invalid source", phone = "1234567890", sourceId })).StatusCode);

        var current = await admin.GetFromJsonAsync<JsonElement>("/api/leads/" + leadId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync("/api/leads/" + leadId, new
            {
                name = "Historical source customer updated", phone = "1234567890",
                sourceId, rowVersion = current.GetProperty("rowVersion").GetString()
            })).StatusCode);

        var sources = await admin.GetFromJsonAsync<JsonElement>("/api/admin/lead-sources");
        var inactive = sources.EnumerateArray().Single(item => item.GetProperty("id").GetInt64() == sourceId);
        Assert.False(inactive.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync("/api/admin/lead-sources/" + sourceId,
                new { name = "Partner Referral", isActive = true })).StatusCode);
        var activeAgain = await admin.GetFromJsonAsync<JsonElement>("/api/masters/lead-sources");
        Assert.Contains(activeAgain.EnumerateArray(),
            item => item.GetProperty("id").GetInt64() == sourceId);
        await using var db = fixture.CreateContext();
        Assert.Equal(sourceId, (await db.Leads.SingleAsync(x => x.Id == leadId)).SourceId);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.TableName == "LeadSources" && x.RecordId == sourceId));
    }

    [Fact]
    public async Task Upgrade_sets_existing_source_rows_active_without_changing_numeric_ids()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync(
            "20261020061147_AddLeadStatusApprovals");
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var sources = await db.LeadSources.OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(new long[] { 1, 2, 3, 4 }, sources.Select(x => x.Id));
        Assert.All(sources, x => Assert.True(x.IsActive));
        Assert.Equal(4, sources.Length);
    }
}
