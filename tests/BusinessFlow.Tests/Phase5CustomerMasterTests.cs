using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase5CustomerMasterTests
{
    [Fact]
    public async Task Admin_manages_multiple_contacts_and_links_two_projects_without_changing_lead_flow()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        var root = "/api/admin/customers";

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.PostAsJsonAsync(root, new { name = "Forbidden client" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(root, new { name = " " })).StatusCode);

        var response = await admin.PostAsJsonAsync(root,
            new { name = "  Example Technology  ", email = "office@example.com" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customerId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        foreach (var name in new[] { "Sales Director", "Technical Manager" })
        {
            Assert.Equal(HttpStatusCode.OK,
                (await admin.PostAsJsonAsync(root + "/" + customerId + "/contacts",
                    new { name, isPrimary = name == "Sales Director" })).StatusCode);
        }

        var current = await admin.GetFromJsonAsync<JsonElement>(root + "/" + customerId);
        Assert.Equal("Example Technology", current.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal(2, current.GetProperty("contacts").GetArrayLength());
        Assert.Single(current.GetProperty("contacts").EnumerateArray()
            .Where(c => c.GetProperty("isPrimary").GetBoolean()));

        var user = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var ownerId = user.GetProperty("id").GetInt64();
        for (var i = 1; i <= 2; i++)
        {
            var newLead = await admin.PostAsJsonAsync("/api/leads",
                new { name = "Example project " + i, phone = "1234567890", sourceId = 1 });
            Assert.Equal(HttpStatusCode.Created, newLead.StatusCode);
            var leadId = (await newLead.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetInt64();
            var lead = await admin.GetFromJsonAsync<JsonElement>("/api/leads/" + leadId);
            var linked = await admin.PutAsJsonAsync(
                root + "/" + customerId + "/leads/" + leadId,
                new { rowVersion = lead.GetProperty("rowVersion").GetString() });
            Assert.Equal(HttpStatusCode.NoContent, linked.StatusCode);

            await using (var db = fixture.CreateContext())
            {
                var record = await db.Leads.SingleAsync(l => l.Id == leadId);
                record.StatusId = 5;
                await db.SaveChangesAsync();
            }
            var project = await admin.PostAsJsonAsync("/api/projects",
                new { leadId, title = "Development project " + i, ownerId });
            Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        }

        var details = await admin.GetFromJsonAsync<JsonElement>(root + "/" + customerId);
        Assert.Equal(2, details.GetProperty("leadCount").GetInt32());
        Assert.Equal(2, details.GetProperty("projectCount").GetInt32());
        Assert.Equal(2, details.GetProperty("projects").GetArrayLength());
        var list = await admin.GetFromJsonAsync<JsonElement>(root + "?search=Example Technology");
        Assert.Contains(list.GetProperty("items").EnumerateArray(),
            c => c.GetProperty("id").GetInt64() == customerId);
        await using var check = fixture.CreateContext();
        Assert.True(await check.AuditLogs.AnyAsync(a => a.TableName == "Customers" && a.RecordId == customerId));
        Assert.Equal(2, await check.Leads.CountAsync(l => l.CustomerId == customerId));
    }

    [Fact]
    public async Task Customer_update_requires_fresh_version_and_lead_reassignment_is_blocked()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var admin = await host.ClientAsync("Admin");
        var root = "/api/admin/customers";
        var first = await admin.PostAsJsonAsync(root, new { name = "First Client" });
        var second = await admin.PostAsJsonAsync(root, new { name = "Second Client" });
        var a = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var b = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var old = await admin.GetFromJsonAsync<JsonElement>(root + "/" + a);
        var version = old.GetProperty("customer").GetProperty("rowVersion").GetString();

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync(root + "/" + a,
                new { name = "First Client Updated", rowVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync(root + "/" + a,
                new { name = "Stale Write", rowVersion = version })).StatusCode);

        var made = await admin.PostAsJsonAsync("/api/leads",
            new { name = "Existing lead", phone = "1234567890", sourceId = 1 });
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var lead = await admin.GetFromJsonAsync<JsonElement>("/api/leads/" + id);
        var rowVersion = lead.GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync(root + "/" + a + "/leads/" + id,
                new { rowVersion })).StatusCode);
        var linked = await admin.GetFromJsonAsync<JsonElement>("/api/leads/" + id);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync(root + "/" + b + "/leads/" + id,
                new { rowVersion = linked.GetProperty("rowVersion").GetString() })).StatusCode);
    }
}
