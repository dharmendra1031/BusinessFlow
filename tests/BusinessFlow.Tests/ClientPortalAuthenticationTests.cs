using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class ClientPortalAuthenticationTests
{
    private sealed class Mailbox : IClientOtpSender
    {
        public bool IsConfigured => true;
        public string? Email { get; private set; }
        public string? Code { get; private set; }
        public int Sent { get; private set; }
        public Task SendAsync(string email, string code, CancellationToken ct)
        {
            Email = email;
            Code = code;
            Sent++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Admin_enrols_contact_otp_authenticates_and_revoke_invalidates_client_session()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        var sender = new Mailbox();
        await using var baseHost = new Phase1ApiHost(database, new Dictionary<string, string?>
        {
            ["ClientPortal:Enabled"] = "true",
            ["ClientPortal:OtpPepper"] = "a_dedicated_server_secret_that_is_at_least_32_bytes_long"
        });
        await using var host = baseHost.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClientOtpSender>();
                services.AddSingleton<IClientOtpSender>(sender);
            }));

        long customerId, contactId;
        await using (var db = database.CreateContext())
        {
            var customer = new Customer { Name = "Customer A" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            var contact = new CustomerContact
            {
                CustomerId = customer.Id, Name = "Contact A",
                Email = "contact-a@example.test", IsPrimary = true
            };
            db.CustomerContacts.Add(contact);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            contactId = contact.Id;
        }

        using var admin = await AdminClient(host);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true
        });

        var create = await admin.PostAsJsonAsync("/api/admin/client-access",
            new { customerId, contactId });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var accountId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.True(accountId > 0);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/admin/client-access",
                new { customerId, contactId })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/client/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/client/auth/request-code",
                new { email = "CONTACT-A@example.test" })).StatusCode);
        Assert.Equal("CONTACT-A@EXAMPLE.TEST", sender.Email);
        Assert.Matches("^[0-9]{8}$", sender.Code!);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/client/auth/verify-code",
                new { email = "contact-a@example.test", code = "not-a-code" })).StatusCode);

        var success = await client.PostAsJsonAsync("/api/client/auth/verify-code",
            new { email = "contact-a@example.test", code = sender.Code });
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Contains(success.Headers.GetValues("Set-Cookie"),
            x => x.Contains("Resmoh.Client=") && x.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/client/auth/verify-code",
                new { email = "contact-a@example.test", code = sender.Code })).StatusCode);

        var me = await client.GetAsync("/api/client/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var data = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Customer A", data.GetProperty("customerName").GetString());
        Assert.False(data.TryGetProperty("customerId", out _));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/customers")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PatchAsJsonAsync($"/api/admin/client-access/{accountId}/status",
                new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/client/me")).StatusCode);

        await using var dbCheck = database.CreateContext();
        Assert.Equal(1, await dbCheck.ClientPortalAccounts.CountAsync());
        Assert.Equal(1, await dbCheck.ClientLoginChallenges.CountAsync());
        Assert.True((await dbCheck.ClientLoginChallenges.SingleAsync()).UsedAtUtc.HasValue);
    }

    [Fact]
    public async Task Unknown_contact_missing_sender_and_wrong_codes_fail_closed()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        var sender = new Mailbox();
        await using var baseHost = new Phase1ApiHost(database, new Dictionary<string, string?>
        {
            ["ClientPortal:Enabled"] = "true",
            ["ClientPortal:OtpPepper"] = "a_dedicated_server_secret_that_is_at_least_32_bytes_long"
        });
        await using var host = baseHost.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClientOtpSender>();
                services.AddSingleton<IClientOtpSender>(sender);
            }));

        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/client/auth/request-code",
                new { email = "unknown@example.test" })).StatusCode);
        Assert.Equal(0, sender.Sent);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/client/auth/verify-code",
                new { email = "unknown@example.test", code = "12345678" })).StatusCode);

        await using var disabledHost = new Phase1ApiHost(database);
        using var disabledClient = disabledHost.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await disabledClient.PostAsJsonAsync("/api/client/auth/request-code",
                new { email = "someone@example.test" })).StatusCode);
    }

    private static async Task<HttpClient> AdminClient(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = "portal-admin@example.test", Email = "portal-admin@example.test",
            FullName = "Portal Admin"
        };
        var created = await users.CreateAsync(user);
        Assert.True(created.Succeeded);
        await users.AddToRoleAsync(user, "Admin");
        var token = scope.ServiceProvider.GetRequiredService<TokenService>()
            .Create(user, ["Admin"]).Token;
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
