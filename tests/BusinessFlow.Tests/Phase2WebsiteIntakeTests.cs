using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2WebsiteIntakeTests
{
    private const string Key = "Website_Ingest_Test_Key_2026_Secure_12345";

    private static HttpRequestMessage Submission(string key, string id, object body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/website/leads")
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Add("X-Website-Key", key);
        message.Headers.Add("X-Submission-Id", id);
        return message;
    }

    private static async Task<(Phase1TestDatabase Database, long OwnerId)> Fixture()
    {
        var database = await Phase1TestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var owner = new ApplicationUser
        {
            FullName = "Website intake owner",
            UserName = "website-owner-" + Guid.NewGuid().ToString("N") + "@test.local",
            IsActive = true
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        return (database, owner.Id);
    }

    [Fact]
    public async Task Website_capture_requires_secret_and_owner_and_rejects_invalid_payload()
    {
        var (database, ownerId) = await Fixture();
        await using var fixture = database;
        var settings = new Dictionary<string, string?>
        {
            ["CRM_WEBSITE_INGEST_KEY"] = Key, ["CRM_WEBSITE_OWNER_ID"] = ownerId.ToString()
        };
        await using var host = new Phase1ApiHost(database, settings);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var lead = new { name = "Website customer", phone = "+96512345678", email = "client@example.test" };

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.SendAsync(Submission("incorrect-secret", "example-ref-0001", lead))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Submission(Key, "short", lead))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Submission(Key, "example-ref-0002",
                new { name = "A", phone = "abc" }))).StatusCode);

        await using var db = database.CreateContext();
        Assert.Equal(0, await db.WebsiteLeadSubmissions.CountAsync());
        Assert.Equal(0, await db.Leads.CountAsync());

        var source = await db.LeadSources.SingleAsync(x => x.Id == 2);
        source.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.SendAsync(Submission(Key, "example-ref-0003", lead))).StatusCode);
        Assert.Empty(await db.Leads.ToListAsync());

        using var unconfigured = new Phase1ApiHost(database, new Dictionary<string, string?>
        {
            ["CRM_WEBSITE_INGEST_KEY"] = "", ["CRM_WEBSITE_OWNER_ID"] = ""
        });
        using var disabled = unconfigured.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await disabled.SendAsync(Submission(Key, "example-ref-0004", lead))).StatusCode);
    }

    [Fact]
    public async Task Website_capture_is_idempotent_and_keeps_history_when_contact_matches()
    {
        var (database, ownerId) = await Fixture();
        await using var fixture = database;
        await using var host = new Phase1ApiHost(database, new Dictionary<string, string?>
        {
            ["CRM_WEBSITE_INGEST_KEY"] = Key, ["CRM_WEBSITE_OWNER_ID"] = ownerId.ToString()
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var lead = new { name = "Website customer", phone = "+965 123-45678",
            email = "person@example.test", companyName = "Test Company", requirement = "Need a website" };

        var created = await client.SendAsync(Submission(Key, "unique-form-ref-0001", lead));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var retried = await client.SendAsync(Submission(Key, "unique-form-ref-0001", lead));
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var repeat = await retried.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, repeat.GetProperty("id").GetInt64());
        Assert.True(repeat.GetProperty("alreadyProcessed").GetBoolean());

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.SendAsync(Submission(Key, "unique-form-ref-0001",
                new { name = "Different", phone = "+965 123-45678",
                    email = "person@example.test", companyName = "Test Company", requirement = "Need a website" }))).StatusCode);

        var another = await client.SendAsync(Submission(Key, "unique-form-ref-0002", lead));
        Assert.Equal(HttpStatusCode.Created, another.StatusCode);
        var anotherId = (await another.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.NotEqual(id, anotherId);

        await using var db = database.CreateContext();
        Assert.Equal(2, await db.Leads.CountAsync());
        Assert.Equal(2, await db.WebsiteLeadSubmissions.CountAsync());
        var saved = await db.Leads.SingleAsync(x => x.Id == id);
        Assert.Equal(ownerId, saved.CreatedById);
        Assert.Equal(ownerId, saved.AssignedToId);
        Assert.Equal(2, saved.SourceId);
        Assert.Equal(1, saved.StatusId);
        Assert.StartsWith("CUS-", saved.CustomerCode);
        Assert.Equal("Website", saved.LeadGivenBy);
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.TableName == "Leads" && x.ChangedFields == "Website intake"));
    }
}
