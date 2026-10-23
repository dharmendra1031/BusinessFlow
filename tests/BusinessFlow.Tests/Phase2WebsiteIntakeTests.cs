using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2WebsiteIntakeTests
{
    private const string Key = "Website_Signed_Test_Key_2026_Secure_1234567890";

    private static async Task<(Phase1TestDatabase Database, long OwnerId)> Fixture()
    {
        var database = await Phase1TestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var owner = new ApplicationUser
        {
            FullName = "Website lead owner",
            UserName = "web-owner-" + Guid.NewGuid().ToString("N") + "@test.local",
            IsActive = true
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        return (database, owner.Id);
    }

    private static HttpRequestMessage Signed(object data, bool incorrectSignature = false, long? timestamp = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data);
        var seconds = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var prefix = Encoding.UTF8.GetBytes(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        var message = new byte[prefix.Length + bytes.Length];
        prefix.CopyTo(message, 0);
        bytes.CopyTo(message, prefix.Length);
        var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), message));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/website/leads")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Website-Timestamp", seconds.ToString());
        request.Headers.Add("X-Website-Signature", "sha256=" +
            (incorrectSignature ? new string('0', 64) : hash));
        return request;
    }

    [Fact]
    public async Task Signed_intake_is_idempotent_and_creates_no_partial_leads()
    {
        var (database, ownerId) = await Fixture();
        await using var fixture = database;
        await using var host = new Phase1ApiHost(database, new Dictionary<string, string?>
        {
            ["WebsiteIntake:Enabled"] = "true",
            ["WebsiteIntake:SigningSecret"] = Key,
            ["WebsiteIntake:OwnerUserId"] = ownerId.ToString()
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        var lead = new { eventId = "unique-website-event-1001", name = "Website customer",
            phone = "+965 123-45678", email = "person@example.test",
            companyName = "Test Company", requirement = "Need a website", consentAccepted = true };

        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Signed(lead))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Signed(lead))).StatusCode);
        var changed = new { eventId = "unique-website-event-1001", name = "Different customer",
            phone = "+965 123-45678", email = "person@example.test",
            companyName = "Test Company", requirement = "Need a website", consentAccepted = true };
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(Signed(changed))).StatusCode);

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Leads.CountAsync());
        Assert.Equal(1, await db.WebsiteLeadSubmissions.CountAsync());
        var stored = await db.Leads.SingleAsync();
        Assert.Equal(ownerId, stored.CreatedById);
        Assert.Equal(ownerId, stored.AssignedToId);
        Assert.Equal(2, stored.SourceId);
        Assert.Equal(1, stored.StatusId);
        Assert.StartsWith("CUS-", stored.CustomerCode);
        Assert.Equal("Website", stored.LeadGivenBy);
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.TableName == "Leads" &&
            x.ChangedFields == "Website signed intake"));

        var sameContactNewEvent = new { eventId = "unique-website-event-1002", name = "Website customer",
            phone = "+965 123-45678", email = "person@example.test",
            companyName = "Test Company", requirement = "Need a website", consentAccepted = true };
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Signed(sameContactNewEvent))).StatusCode);
        Assert.Equal(2, await db.Leads.CountAsync()); // Reuse of contact triggers review, not destructive merging.
    }

    [Fact]
    public async Task Website_intake_rejects_missing_config_invalid_signature_expiry_and_consent()
    {
        var (database, ownerId) = await Fixture();
        await using var fixture = database;
        await using var host = new Phase1ApiHost(database, new Dictionary<string, string?>
        {
            ["WebsiteIntake:Enabled"] = "true",
            ["WebsiteIntake:SigningSecret"] = Key,
            ["WebsiteIntake:OwnerUserId"] = ownerId.ToString()
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        var payload = new { eventId = "website-event-2001", name = "Visitor",
            phone = "9876543210", consentAccepted = true };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.SendAsync(Signed(payload, incorrectSignature: true))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.SendAsync(Signed(payload, timestamp: DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeSeconds()))).StatusCode);
        var noConsent = new { eventId = "website-event-2002", name = "Visitor",
            phone = "9876543210", consentAccepted = false };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Signed(noConsent))).StatusCode);
        var invalidPhone = new { eventId = "website-event-2003", name = "Visitor",
            phone = "123", consentAccepted = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Signed(invalidPhone))).StatusCode);

        await using (var db = database.CreateContext())
        {
            Assert.Equal(0, await db.Leads.CountAsync());
            var source = await db.LeadSources.SingleAsync(x => x.Id == 2);
            source.IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(Signed(payload))).StatusCode);

        await using var disabledHost = new Phase1ApiHost(database);
        using var disabledClient = disabledHost.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await disabledClient.SendAsync(Signed(payload))).StatusCode);
        await using var finalDb = database.CreateContext();
        Assert.Equal(0, await finalDb.WebsiteLeadSubmissions.CountAsync());
        Assert.Equal(0, await finalDb.Leads.CountAsync());
    }
}
