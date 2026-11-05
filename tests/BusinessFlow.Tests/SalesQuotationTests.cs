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
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{root}/{id}/status",
                new { status = "Sent", rowVersion = currentVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{root}/{id}/submit",
                new { rowVersion = currentVersion })).StatusCode);
        detail = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        currentVersion = detail.GetProperty("quotation").GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"{root}/{id}/review",
                new { approve = true, comment = "Too short", rowVersion = currentVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{root}/{id}/review",
                new { approve = true, comment = "Validated by commercial administrator",
                    rowVersion = currentVersion })).StatusCode);
        detail = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        currentVersion = detail.GetProperty("quotation").GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{root}/{id}/status",
                new { status = "Sent", rowVersion = currentVersion })).StatusCode);
        detail = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        currentVersion = detail.GetProperty("quotation").GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"{root}/{id}/status",
                new { status = "Accepted", rowVersion = currentVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{root}/{id}/status",
                new { status = "Accepted", rowVersion = currentVersion,
                    clientResponseBy = "Client owner", clientResponseReference = "Email dated 2026-11-05" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{root}/{id}/revisions", new { rowVersion = currentVersion, details = create.details })).StatusCode);
        var accepted = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        var acceptedVersion = accepted.GetProperty("quotation").GetProperty("rowVersion").GetString();
        var contractRequest = new { terms = "Delivery scope and payment milestones agreed in writing",
            rowVersion = acceptedVersion };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.PostAsJsonAsync($"{root}/{id}/contract", contractRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"{root}/{id}/contract", contractRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{root}/{id}/contract", contractRequest)).StatusCode);
        var withContract = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        var contract = withContract.GetProperty("contract");
        Assert.Equal("Draft", contract.GetProperty("status").GetString());
        Assert.Equal(2, contract.GetProperty("quotationRevision").GetInt32());
        var contractVersion = contract.GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"{root}/{id}/contract/sign",
                new { signedBy = "Client", signatureReference = "bad", signedOn = DateOnly.FromDateTime(DateTime.UtcNow),
                    rowVersion = contractVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{root}/{id}/contract/sign",
                new { signedBy = "Client owner", signatureReference = "Signed PDF document reference",
                    signedOn = DateOnly.FromDateTime(DateTime.UtcNow), rowVersion = contractVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{root}/{id}/contract/sign",
                new { signedBy = "Client owner", signatureReference = "Signed PDF document reference",
                    signedOn = DateOnly.FromDateTime(DateTime.UtcNow), rowVersion = contractVersion })).StatusCode);
        var signed = await admin.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        Assert.Equal("Signed", signed.GetProperty("contract").GetProperty("status").GetString());
        Assert.Equal("Client owner", signed.GetProperty("quotation").GetProperty("clientResponseBy").GetString());
        await using var check = fixture.CreateContext();
        Assert.Equal(2, await check.SalesQuotationRevisions.CountAsync(r => r.QuotationId == id));
        Assert.True(await check.AuditLogs.AnyAsync(a => a.TableName == "SalesQuotations" && a.RecordId == id && a.Action == "Status"));
    }
    [Fact]
    public async Task Itemized_quotation_calculates_discounts_and_taxes_without_trusting_client_amount()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var admin = await host.ClientAsync("Admin");
        var userId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        long leadId;
        await using (var db = fixture.CreateContext())
        {
            var lead = new Lead { Name = "Itemized quote", Phone = "1234567890",
                SourceId = 1, StatusId = 3, CreatedById = userId };
            db.Leads.Add(lead);
            await db.SaveChangesAsync();
            leadId = lead.Id;
        }
        var details = new
        {
            title = "Website implementation", currency = "INR", amount = 1m,
            items = new[]
            {
                new { description = "UI", quantity = 2m, unitPrice = 1000m, discountRate = 10m, taxRate = 18m },
                new { description = "API", quantity = 1m, unitPrice = 500m, discountRate = 0m, taxRate = 0m }
            }
        };
        var response = await admin.PostAsJsonAsync("/api/admin/quotations", new { leadId, details });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var result = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/quotations/{id}");
        var revision = result.GetProperty("revisions")[0];
        Assert.Equal(2624m, revision.GetProperty("amount").GetDecimal());
        Assert.Equal(2500m, revision.GetProperty("subtotal").GetDecimal());
        Assert.Equal(200m, revision.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(324m, revision.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(2, revision.GetProperty("items").GetArrayLength());

        var invalid = await admin.PostAsJsonAsync($"/api/admin/quotations/{id}/revisions",
            new { rowVersion = result.GetProperty("quotation").GetProperty("rowVersion").GetString(),
                details = new { title = "Bad quote", currency = "INR", items = new[]
                {
                    new { description = "Invalid", quantity = 1m, unitPrice = 100m, taxRate = 120m }
                } } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using var check = fixture.CreateContext();
        Assert.Equal(2, await check.SalesQuotationLineItems.CountAsync());
    }
}
