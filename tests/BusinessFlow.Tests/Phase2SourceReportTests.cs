using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2SourceReportTests
{
    [Fact]
    public async Task Source_reports_filter_the_actual_report_scope_and_keep_historical_sources()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var employee = await host.ClientAsync("Employee");
        using var outsider = await host.ClientAsync("Employee");
        using var admin = await host.ClientAsync("Admin");
        var me = (await employee.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        var other = (await outsider.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();

        await using (var db = database.CreateContext())
        {
            db.Leads.AddRange(
                new Lead { Name = "My won website", Phone = "9876500001",
                    SourceId = 2, StatusId = 5, AssignedToId = me, CreatedById = me,
                    CustomerCode = "TEST-SRC-1", ProjectAmount = 1500.250m },
                new Lead { Name = "My qualified website", Phone = "9876500002",
                    SourceId = 2, StatusId = 3, AssignedToId = me, CreatedById = me,
                    CustomerCode = "TEST-SRC-2", ProjectAmount = 200m },
                new Lead { Name = "My manual lead", Phone = "9876500003",
                    SourceId = 1, StatusId = 1, AssignedToId = me, CreatedById = me,
                    CustomerCode = "TEST-SRC-3" },
                new Lead { Name = "Private won website", Phone = "9876500004",
                    SourceId = 2, StatusId = 5, AssignedToId = other, CreatedById = other,
                    CustomerCode = "TEST-SRC-4", ProjectAmount = 9000m });
            await db.SaveChangesAsync();
            // Historical source names remain reportable after deactivation.
            var website = await db.LeadSources.SingleAsync(x => x.Id == 2);
            website.IsActive = false;
            await db.SaveChangesAsync();
        }

        var own = await employee.GetFromJsonAsync<JsonElement>(
            "/api/reports?type=source-summary&sourceId=2");
        Assert.Equal(1, own.GetProperty("total").GetInt32());
        var summary = own.GetProperty("rows")[0];
        Assert.Equal("Website", summary.GetProperty("sourceName").GetString());
        Assert.Equal(2, summary.GetProperty("leadCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("qualifiedCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("wonCount").GetInt32());
        Assert.Equal(50m, summary.GetProperty("conversionPercent").GetDecimal());
        Assert.Equal(1700.250m, summary.GetProperty("projectAmount").GetDecimal());

        var details = await employee.GetFromJsonAsync<JsonElement>(
            "/api/reports?type=source-details&sourceId=2");
        Assert.Equal(2, details.GetProperty("total").GetInt32());
        var detailRows = details.GetProperty("rows").EnumerateArray().ToList();
        Assert.Contains(detailRows, x => x.GetProperty("customerName").GetString() == "My won website");
        Assert.DoesNotContain(detailRows, x =>
            x.GetProperty("customerName").GetString() == "Private won website");

        var filtered = await employee.GetFromJsonAsync<JsonElement>(
            "/api/reports?type=source-summary&sourceId=2&statusId=5");
        Assert.Equal(1, filtered.GetProperty("rows")[0].GetProperty("leadCount").GetInt32());
        Assert.Equal(100m, filtered.GetProperty("rows")[0].GetProperty("conversionPercent").GetDecimal());
        var acrossSources = await employee.GetFromJsonAsync<JsonElement>(
            "/api/reports?type=source-summary");
        Assert.Equal(2, acrossSources.GetProperty("total").GetInt32());

        var adminTotals = await admin.GetFromJsonAsync<JsonElement>(
            "/api/reports?type=source-summary&sourceId=2");
        Assert.Equal(3, adminTotals.GetProperty("rows")[0].GetProperty("leadCount").GetInt32());

        var csv = await employee.GetStringAsync("/api/reports/export?type=source-details&sourceId=2");
        Assert.Contains("My won website", csv);
        Assert.DoesNotContain("Private won website", csv);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await employee.GetAsync("/api/reports?type=source-summary&sourceId=-1")).StatusCode);
    }
}
