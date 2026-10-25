using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase5CustomerDirectoryAccessTests
{
    private static async Task<long> CurrentUserId(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();

    [Fact]
    public async Task Customer_directory_is_scoped_and_does_not_expose_billing_or_contacts()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var manager = await host.ClientAsync("Manager");
        using var employee = await host.ClientAsync("Employee");
        using var outsider = await host.ClientAsync("Employee");
        using var admin = await host.ClientAsync("Admin");
        var managerId = await CurrentUserId(manager);
        var employeeId = await CurrentUserId(employee);
        var outsiderId = await CurrentUserId(outsider);

        long visibleCustomer;
        long privateCustomer;
        await using (var db = fixture.CreateContext())
        {
            var report = await db.Users.SingleAsync(u => u.Id == employeeId);
            report.ManagerId = managerId;
            var visible = new Customer { Name = "Team client", Email = "private@example.com",
                Gstin = "09ABCDE1234F1Z5", BillingAddress = "Private billing address" };
            var hidden = new Customer { Name = "Other client", Phone = "919876543210" };
            db.Customers.AddRange(visible, hidden);
            await db.SaveChangesAsync();
            visibleCustomer = visible.Id;
            privateCustomer = hidden.Id;
            db.CustomerContacts.Add(new CustomerContact
            {
                CustomerId = visible.Id, Name = "Private contact", Email = "contact@example.com"
            });
            db.Leads.AddRange(
                new Lead { Name = "Sales lead", Phone = "1234567890", SourceId = 1,
                    CreatedById = managerId, AssignedToId = employeeId, CustomerId = visible.Id },
                new Lead { Name = "Private lead", Phone = "1234567891", SourceId = 1,
                    CreatedById = outsiderId, AssignedToId = outsiderId, CustomerId = hidden.Id });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.GetAsync("/api/customers")).StatusCode);
        var overview = await manager.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.Equal(1, overview.GetProperty("total").GetInt32());
        var item = overview.GetProperty("items")[0];
        Assert.Equal(visibleCustomer, item.GetProperty("id").GetInt64());
        Assert.False(item.TryGetProperty("email", out _));
        Assert.False(item.TryGetProperty("gstin", out _));
        Assert.False(item.TryGetProperty("phone", out _));

        var details = await manager.GetFromJsonAsync<JsonElement>("/api/customers/" + visibleCustomer);
        Assert.Equal("Team client", details.GetProperty("customer").GetProperty("name").GetString());
        Assert.False(details.GetProperty("customer").TryGetProperty("billingAddress", out _));
        Assert.False(details.TryGetProperty("contacts", out _));
        Assert.Single(details.GetProperty("leads").EnumerateArray());

        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.GetAsync("/api/customers/" + privateCustomer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync("/api/admin/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync("/api/admin/customers", new { name = "Denied write" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync("/api/admin/customers/" + visibleCustomer)).StatusCode);
    }

    [Fact]
    public async Task Employee_requires_explicit_grant_and_user_deny_wins()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var employee = await host.ClientAsync("Employee");
        var userId = await CurrentUserId(employee);
        await using var db = fixture.CreateContext();
        var customer = new Customer { Name = "Owned account" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.Leads.Add(new Lead { Name = "My lead", Phone = "1234567890", SourceId = 1,
            CreatedById = userId, AssignedToId = userId, CustomerId = customer.Id });
        await db.SaveChangesAsync();
        var roles = new RoleAdministrationService(db);
        var id = await roles.CreateRoleAsync("Customer reader", userId);
        await roles.SetGrantAsync(id, "customers.view", DataScope.Self, userId);
        await roles.SetMemberAsync(id, userId, true, userId);
        using var permitted = await host.ClientForUserAsync(userId);
        var result = await permitted.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.Equal(1, result.GetProperty("total").GetInt32());
        Assert.Equal(customer.Id, result.GetProperty("items")[0].GetProperty("id").GetInt64());
        await roles.SetRestrictionAsync(userId, "customers.view", true, userId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await permitted.GetAsync("/api/customers")).StatusCode);
    }
}
