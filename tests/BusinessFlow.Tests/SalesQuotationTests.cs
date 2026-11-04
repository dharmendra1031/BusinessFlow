using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class SalesQuotationTests
{
    [Fact]
    public async Task Quotation_versions_are_immutable_and_transitions_require_current_version()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        var userId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        long leadId;
        await using (var db = fixture.CreateContext())
        {
            var lead = new Lead { Name = "Quotation client", Phone = "1234567890",
                SourceId = 1, StatusId = 3, CreatedById = userId };
            db.Leads.Add(lead);
            await db.SaveChangesAsync();
            leadId = lead.Id;
        }

        var create = new { leadId, details = new { title = "Website", scope = "Initial delivery",
            amount = 25000.000m, currency = "inr", validUntil = (DateOnly?)null,
            paymentTerms = "50% advance" } };
        const string root = "/api/admin/quotations";
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync(root, create)).StatusCode);
        var created = await admin.PostAsJsonAsync(root, create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(root, create)).StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var first = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        var oldVersion = first.GetProperty("quotation").GetProperty("rowVersion").GetString();
        Assert.Equal("INR", first.GetProperty("revisions")[0].GetProperty("currency").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{root}/{id}/status", new { status = "Accepted", rowVersion = oldVersion })).StatusCode);
        var second = await admin.PostAsJsonAsync($"{root}/{id}/revisions", new
        {
            rowVersion = oldVersion, details = new { title = "Website phase 2", scope = "Revised scope",
                amount = 30000.125m, currency = "KWD", paymentTerms = "At milestones" }
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{root}/{id}/status", new { status = "Sent", rowVersion = oldVersion })).StatusCode);

        var detail = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        Assert.Equal(2, detail.GetProperty("revisions").GetArrayLength());
        Assert.Equal(2, detail.GetProperty("quotation").GetProperty("currentRevision").GetInt32());
        Assert.Equal("Website", detail.GetProperty("revisions")[1].GetProperty("title").GetString());
        var currentVersion = detail.GetProperty("quotation").GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{root}/{id}/status", new { status = "Sent", rowVersion = currentVersion })).StatusCode);
        detail = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        currentVersion = detail.GetProperty("quotation").GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{root}/{id}/status", new { status = "Accepted", rowVersion = currentVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{root}/{id}/revisions", new { rowVersion = currentVersion, details = create.details })).StatusCode);
        await using var check = fixture.CreateContext();
        Assert.Equal(2, await check.SalesQuotationRevisions.CountAsync(r => r.QuotationId == id));
        Assert.True(await check.AuditLogs.AnyAsync(a => a.TableName == "SalesQuotations" && a.RecordId == id && a.Action == "Status"));
    }
}
