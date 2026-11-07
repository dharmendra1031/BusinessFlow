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

    [Fact]
    public async Task Client_documents_are_read_only_and_limited_to_the_verified_customer()
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

        using var admin = await AdminClient(host);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true
        });
        var staffId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        long customerId, contactId, sentId, draftId, otherId, invoiceId, otherInvoiceId;

        await using (var db = database.CreateContext())
        {
            var customer = new Customer { Name = "Company A" };
            var other = new Customer { Name = "Company B" };
            db.Customers.AddRange(customer, other);
            await db.SaveChangesAsync();

            var contact = new CustomerContact
            {
                CustomerId = customer.Id, Name = "A Contact", Email = "a@example.test"
            };
            db.CustomerContacts.Add(contact);

            Lead NewLead(long customer, string name) => new()
            {
                CustomerId = customer, Name = name, Phone = "1234567890",
                SourceId = 1, StatusId = 3, CreatedById = staffId
            };
            var sentLead = NewLead(customer.Id, "A Sent");
            var draftLead = NewLead(customer.Id, "A Draft");
            var acceptedLead = NewLead(customer.Id, "A Accepted");
            var otherLead = NewLead(other.Id, "B Accepted");
            db.Leads.AddRange(sentLead, draftLead, acceptedLead, otherLead);
            await db.SaveChangesAsync();

            SalesQuotation NewQuote(long leadId, string status) => new()
            {
                LeadId = leadId, Status = status, CreatedById = staffId
            };
            var sent = NewQuote(sentLead.Id, "Sent");
            var draft = NewQuote(draftLead.Id, "Draft");
            var accepted = NewQuote(acceptedLead.Id, "Accepted");
            var foreign = NewQuote(otherLead.Id, "Accepted");
            db.SalesQuotations.AddRange(sent, draft, accepted, foreign);
            await db.SaveChangesAsync();

            SalesQuotationRevision NewRevision(long quotationId, string title) => new()
            {
                QuotationId = quotationId, Revision = 1, Title = title,
                Amount = 100m, Subtotal = 100m, CreatedById = staffId
            };
            db.SalesQuotationRevisions.AddRange(
                NewRevision(sent.Id, "A Public Quote"),
                NewRevision(draft.Id, "A Private Draft"),
                NewRevision(accepted.Id, "A Signed Quote"),
                NewRevision(foreign.Id, "B Private Quote"));
            await db.SaveChangesAsync();

            var contract = new SalesContract
            {
                QuotationId = accepted.Id, QuotationRevision = 1,
                BilledAmount = 100m, Status = "Signed", Terms = "Agreed terms",
                CreatedById = staffId
            };
            var otherContract = new SalesContract
            {
                QuotationId = foreign.Id, QuotationRevision = 1,
                BilledAmount = 100m, Status = "Signed", Terms = "Other terms",
                CreatedById = staffId
            };
            db.SalesContracts.AddRange(contract, otherContract);
            await db.SaveChangesAsync();

            SalesInvoice NewInvoice(long contractId, string status) => new()
            {
                ContractId = contractId, Status = status, Amount = 100m,
                Description = "Project milestone", BillToName = "Customer",
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                CreatedById = staffId
            };
            var invoice = NewInvoice(contract.Id, "Issued");
            var voidInvoice = NewInvoice(contract.Id, "Void");
            var otherInvoice = NewInvoice(otherContract.Id, "Issued");
            db.SalesInvoices.AddRange(invoice, voidInvoice, otherInvoice);
            await db.SaveChangesAsync();

            customerId = customer.Id;
            contactId = contact.Id;
            sentId = sent.Id;
            draftId = draft.Id;
            otherId = foreign.Id;
            invoiceId = invoice.Id;
            otherInvoiceId = otherInvoice.Id;
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/client/quotations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await admin.GetAsync("/api/client/invoices")).StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync("/api/admin/client-access", new { customerId, contactId })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/client/auth/request-code",
                new { email = "a@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/client/auth/verify-code",
                new { email = "a@example.test", code = sender.Code })).StatusCode);

        var quotes = await client.GetFromJsonAsync<JsonElement>("/api/client/quotations");
        Assert.Equal(2, quotes.GetProperty("total").GetInt32());
        Assert.Contains(quotes.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetInt64() == sentId);
        Assert.DoesNotContain(quotes.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("title").GetString() == "A Private Draft" ||
                 x.GetProperty("title").GetString() == "B Private Quote");
        Assert.False(quotes.GetProperty("items")[0].TryGetProperty("leadId", out _));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/client/quotations/{draftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/client/quotations/{otherId}")).StatusCode);
        var visibleQuote = await client.GetFromJsonAsync<JsonElement>($"/api/client/quotations/{sentId}");
        Assert.Equal("A Public Quote", visibleQuote.GetProperty("title").GetString());
        Assert.False(visibleQuote.TryGetProperty("createdById", out _));

        var invoices = await client.GetFromJsonAsync<JsonElement>("/api/client/invoices");
        Assert.Equal(1, invoices.GetProperty("total").GetInt32());
        Assert.Equal(invoiceId, invoices.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.False(invoices.GetProperty("items")[0].TryGetProperty("contractId", out _));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/client/invoices/{otherInvoiceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/client/invoices/{invoiceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/client/invoices?page=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/billing/invoices")).StatusCode);

        await using (var db = database.CreateContext())
        {
            var account = await db.ClientPortalAccounts.SingleAsync();
            account.IsActive = false;
            account.SessionVersion++;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/client/quotations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/client/invoices")).StatusCode);
    }

    [Fact]
    public async Task Client_only_sees_explicitly_published_milestones_for_their_customer()
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

        using var admin = await AdminClient(host);
        using var employee = await baseHost.ClientAsync("Employee");
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true
        });
        var staffId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();

        long customerId, contactId, projectId, sharedId, hiddenId, foreignProjectId, foreignId;
        await using (var db = database.CreateContext())
        {
            var own = new Customer { Name = "Portal Customer" };
            var foreign = new Customer { Name = "Another Customer" };
            db.Customers.AddRange(own, foreign);
            await db.SaveChangesAsync();

            var contact = new CustomerContact
            {
                CustomerId = own.Id, Name = "Portal Contact", Email = "milestones@example.test"
            };
            db.CustomerContacts.Add(contact);
            var lead = new Lead
            {
                CustomerId = own.Id, Name = "Own Lead", Phone = "1234567890",
                SourceId = 1, StatusId = 5, CreatedById = staffId
            };
            var foreignLead = new Lead
            {
                CustomerId = foreign.Id, Name = "Other Lead", Phone = "1234567891",
                SourceId = 1, StatusId = 5, CreatedById = staffId
            };
            db.Leads.AddRange(lead, foreignLead);
            await db.SaveChangesAsync();

            var ownProject = new DevelopmentProject
            {
                LeadId = lead.Id, OwnerId = staffId, CreatedById = staffId,
                Title = "Customer Website", Status = "Planning"
            };
            var otherProject = new DevelopmentProject
            {
                LeadId = foreignLead.Id, OwnerId = staffId, CreatedById = staffId,
                Title = "Private Project", Status = "Planning"
            };
            db.DevelopmentProjects.AddRange(ownProject, otherProject);
            await db.SaveChangesAsync();

            var shared = new ProjectMilestone { ProjectId = ownProject.Id, Title = "Client review" };
            var hidden = new ProjectMilestone { ProjectId = ownProject.Id, Title = "Internal QA" };
            var other = new ProjectMilestone { ProjectId = otherProject.Id, Title = "Other client work" };
            db.ProjectMilestones.AddRange(shared, hidden, other);
            await db.SaveChangesAsync();
            customerId = own.Id;
            contactId = contact.Id;
            projectId = ownProject.Id;
            foreignProjectId = otherProject.Id;
            sharedId = shared.Id;
            hiddenId = hidden.Id;
            foreignId = other.Id;
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/client/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await admin.GetAsync("/api/client/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync("/api/admin/client-access", new { customerId, contactId })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/client/auth/request-code",
                new { email = "milestones@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/client/auth/verify-code",
                new { email = "milestones@example.test", code = sender.Code })).StatusCode);

        var empty = await client.GetFromJsonAsync<JsonElement>("/api/client/projects");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());

        var publish = new { isPublished = true };
        var ownUrl = $"/api/projects/{projectId}/planning/milestones/{sharedId}/publication";
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.PatchAsJsonAsync(ownUrl, publish)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PatchAsJsonAsync(ownUrl, publish)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PatchAsJsonAsync(
                $"/api/projects/{foreignProjectId}/planning/milestones/{foreignId}/publication",
                publish)).StatusCode);

        var visible = await client.GetFromJsonAsync<JsonElement>("/api/client/projects");
        Assert.Equal(1, visible.GetProperty("total").GetInt32());
        var update = visible.GetProperty("items")[0];
        Assert.Equal(sharedId, update.GetProperty("id").GetInt64());
        Assert.Equal("Customer Website", update.GetProperty("projectTitle").GetString());
        Assert.Equal("Client review", update.GetProperty("title").GetString());
        Assert.False(update.TryGetProperty("description", out _));
        Assert.False(update.TryGetProperty("ownerId", out _));
        Assert.False(update.TryGetProperty("customerId", out _));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/client/projects?page=-1")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync(
                $"/api/projects/{projectId}/planning/milestones/{sharedId}",
                new { title = "Client review updated", description = "Revised", status = "Planned", dueDate = (DateOnly?)null })).StatusCode);
        var afterEdit = await client.GetFromJsonAsync<JsonElement>("/api/client/projects");
        Assert.Equal(0, afterEdit.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PatchAsJsonAsync(ownUrl, publish)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PatchAsJsonAsync(ownUrl, new { isPublished = false })).StatusCode);
        var afterHide = await client.GetFromJsonAsync<JsonElement>("/api/client/projects");
        Assert.Equal(0, afterHide.GetProperty("total").GetInt32());

        await using var dbCheck = database.CreateContext();
        Assert.Null((await dbCheck.ProjectMilestones.SingleAsync(x => x.Id == sharedId)).PublishedAtUtc);
        Assert.Null((await dbCheck.ProjectMilestones.SingleAsync(x => x.Id == hiddenId)).PublishedAtUtc);
        Assert.NotNull((await dbCheck.ProjectMilestones.SingleAsync(x => x.Id == foreignId)).PublishedAtUtc);
        Assert.True(await dbCheck.AuditLogs.AnyAsync(x =>
            x.TableName == "ProjectMilestones" && x.RecordId == sharedId && x.Action == "Unpublish"));
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
