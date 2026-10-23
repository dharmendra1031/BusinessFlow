using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2SourceReportTests
{
    [Fact]
    public async Task Source_summary_aggregates_only_permitted_leads_and_respects_filters()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        using var outsider = await host.ClientAsync("Employee");
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var mine = (await employee.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var other = (await outsider.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        await using (var db = database.CreateContext())
        {
            db.Leads.AddRange(
                Lead("Manual new", 1, 1, mine, 100m, 15),
                Lead("Manual won", 1, 5, mine, 400m, 16),
                Lead("Website qualified", 2, 3, mine, 200m, 16),
                Lead("Outside won", 1, 5, other, 800m, 17));
            await db.SaveChangesAsync();
        }

        var restricted = await employee.GetFromJsonAsync<JsonElement>("/api/reports?type=source-wise");
        Assert.Equal(2, restricted.GetProperty("total").GetInt32());
        var own = restricted.GetProperty("rows").EnumerateArray().ToArray();
        var manual = own.Single(x => x.GetProperty("sourceId").GetInt64() == 1);
        Assert.Equal("Manual", manual.GetProperty("sourceName").GetString());
        Assert.Equal(2, manual.GetProperty("leadCount").GetInt64());
        Assert.Equal(1, manual.GetProperty("qualifiedCount").GetInt64());
        Assert.Equal(1, manual.GetProperty("wonCount").GetInt64());
        Assert.Equal(50m, manual.GetProperty("conversionRate").GetDecimal());
        Assert.Equal(500m, manual.GetProperty("pipelineAmount").GetDecimal());
        Assert.Equal(400m, manual.GetProperty("wonAmount").GetDecimal());
        Assert.Equal(1, own.Single(x => x.GetProperty("sourceId").GetInt64() == 2)
            .GetProperty("qualifiedCount").GetInt64());

        var all = await admin.GetFromJsonAsync<JsonElement>("/api/reports?type=source-wise");
        var globalManual = all.GetProperty("rows").EnumerateArray()
            .Single(x => x.GetProperty("sourceId").GetInt64() == 1);
        Assert.Equal(3, globalManual.GetProperty("leadCount").GetInt64());
        Assert.Equal(1200m, globalManual.GetProperty("wonAmount").GetDecimal());

        var filtered = await employee.GetFromJsonAsync<JsonElement>(
            "/api/reports?type=source-wise&sourceId=2&from=2026-01-16&to=2026-01-16");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(1, filtered.GetProperty("rows")[0].GetProperty("leadCount").GetInt64());

        var status = await employee.GetFromJsonAsync<JsonElement>(
            "/api/reports?type=source-wise&statusId=5&userId=" + mine);
        Assert.Equal(1, status.GetProperty("total").GetInt32());
        Assert.Equal(1, status.GetProperty("rows")[0].GetProperty("wonCount").GetInt64());

        var csv = await employee.GetStringAsync("/api/reports/export?type=source-wise");
        Assert.Contains("sourceName", csv);
        Assert.Contains("Manual", csv);
        Assert.DoesNotContain("Outside won", csv);
        Assert.DoesNotContain("1200", csv); // private Won amount must not leak
    }

    [Fact]
    public async Task A_broader_leads_view_grant_never_broadens_source_report_scope()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var employee = await host.ClientAsync("Employee");
        using var outsider = await host.ClientAsync("Employee");
        var mine = (await employee.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var other = (await outsider.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        await using var db = database.CreateContext();
        db.Leads.AddRange(Lead("Mine", 1, 1, mine, 50m, 15), Lead("Foreign", 2, 5, other, 1000m, 15));
        await db.SaveChangesAsync();
        var roleService = new RoleAdministrationService(db);
        var grant = await roleService.CreateRoleAsync("Broad lead reader source check", other);
        await roleService.SetGrantAsync(grant, "leads.view", DataScope.Organization, other);
        await roleService.SetMemberAsync(grant, mine, true, other);
        using var session = await host.ClientForUserAsync(mine);

        Assert.Equal(2, (await session.GetFromJsonAsync<JsonElement>("/api/leads"))
            .GetProperty("total").GetInt32());
        var report = await session.GetFromJsonAsync<JsonElement>("/api/reports?type=source-wise");
        Assert.Equal(1, report.GetProperty("total").GetInt32());
        Assert.Equal(1, report.GetProperty("rows")[0].GetProperty("leadCount").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/reports/export?type=source-wise")).StatusCode);

        await roleService.SetRestrictionAsync(mine, "reports.view", true, other);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.GetAsync("/api/reports?type=source-wise")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.GetAsync("/api/reports/pdf?type=source-wise")).StatusCode);
    }


    [Fact]
    public async Task Source_details_list_only_authorized_leads_with_source_and_assignee_names()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var employee = await host.ClientAsync("Employee");
        using var outsider = await host.ClientAsync("Employee");
        var mine = (await employee.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var other = (await outsider.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        await using (var db = database.CreateContext())
        {
            db.Leads.AddRange(
                Lead("My website visitor", 2, 3, mine, 175.25m, 16),
                Lead("My manual visitor", 1, 1, mine, 75m, 16),
                Lead("Private website visitor", 2, 5, other, 900m, 16));
            await db.SaveChangesAsync();
        }

        var result = await employee.GetFromJsonAsync<JsonElement>(
            "/api/reports?type=source-details&sourceId=2&from=2026-01-16&to=2026-01-16");
        Assert.Equal(1, result.GetProperty("total").GetInt32());
        var row = result.GetProperty("rows")[0];
        Assert.Equal("Website", row.GetProperty("sourceName").GetString());
        Assert.Equal("My website visitor", row.GetProperty("customerName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("assignedUser").GetString()));
        Assert.Equal("Qualified", row.GetProperty("leadStatus").GetString());
        Assert.Equal(175.25m, row.GetProperty("projectAmount").GetDecimal());
        Assert.True(row.TryGetProperty("createdAtUtc", out _));

        var csv = await employee.GetStringAsync("/api/reports/export?type=source-details&sourceId=2");
        Assert.Contains("My website visitor", csv);
        Assert.DoesNotContain("Private website visitor", csv);
        Assert.DoesNotContain("900", csv);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await employee.GetAsync("/api/reports?type=not-a-source-report")).StatusCode);
    }

    private static Lead Lead(string name, long sourceId, long statusId, long userId, decimal amount, int day) =>
        new()
        {
            Name = name, Phone = "9876543210", SourceId = sourceId, StatusId = statusId,
            CreatedById = userId, AssignedToId = userId, ProjectAmount = amount,
            CreatedAtUtc = new DateTime(2026, 1, day, 12, 0, 0, DateTimeKind.Utc)
        };
}
