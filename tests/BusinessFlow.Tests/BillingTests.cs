using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class BillingTests
{
    private const string Root = "/api/admin/billing";

    private static async Task<(long ContractId, string Version)> SignedContract(
        Phase1TestDatabase database, long userId, decimal amount = 1000m)
    {
        await using var db = database.CreateContext();
        var lead = new Lead
        {
            Name = "Billing customer", CompanyName = "Test client ltd",
            Phone = "1234567890", SourceId = 1, StatusId = 3, CreatedById = userId
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        var quotation = new SalesQuotation
        {
            LeadId = lead.Id, Status = "Accepted", CreatedById = userId
        };
        db.SalesQuotations.Add(quotation);
        await db.SaveChangesAsync();
        db.SalesQuotationRevisions.Add(new SalesQuotationRevision
        {
            QuotationId = quotation.Id, Revision = 1, Title = "Signed project scope",
            Amount = amount, Subtotal = amount, Currency = "INR", CreatedById = userId
        });
        var contract = new SalesContract
        {
            QuotationId = quotation.Id, QuotationRevision = 1,
            Status = "Signed", Terms = "Agreed services and milestones",
            SignedBy = "Client", SignatureReference = "Signed contract reference",
            SignedOn = DateOnly.FromDateTime(DateTime.UtcNow), CreatedById = userId
        };
        db.SalesContracts.Add(contract);
        await db.SaveChangesAsync();
        return (contract.Id, Convert.ToBase64String(contract.RowVersion));
    }

    [Fact]
    public async Task Signed_contract_billing_and_partial_payments_preserve_balance_and_block_duplicates()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var (contractId, version) = await SignedContract(fixture, me.GetProperty("id").GetInt64());
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        var request = new
        {
            contractId, contractRowVersion = version, type = "Advance",
            description = "Advance on signed software project", amount = 300m, dueDate
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.PostAsJsonAsync(Root + "/invoices", request)).StatusCode);
        var created = await admin.PostAsJsonAsync(Root + "/invoices", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invoiceId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{invoiceId}");
        var invoiceVersion = detail.GetProperty("invoice").GetProperty("rowVersion").GetString();
        Assert.Equal(300m, detail.GetProperty("invoice").GetProperty("outstanding").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/payments", new
            {
                amount = 100m, method = "UPI", reference = "UPI-INVOICE-1",
                receivedOn = DateOnly.FromDateTime(DateTime.UtcNow), invoiceRowVersion = "bad"
            })).StatusCode);

        var payment = new
        {
            amount = 200m, method = "BankTransfer", reference = "BANK-TEST-20261105-001",
            receivedOn = DateOnly.FromDateTime(DateTime.UtcNow), invoiceRowVersion = invoiceVersion
        };
        var paid = await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/payments", payment);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(100m, (await paid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outstanding").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/payments", payment)).StatusCode);
        detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{invoiceId}");
        invoiceVersion = detail.GetProperty("invoice").GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/payments", new
            {
                amount = 101m, method = "Cash", reference = "CASH-OVERPAY-1001",
                receivedOn = DateOnly.FromDateTime(DateTime.UtcNow), invoiceRowVersion = invoiceVersion
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/void", new
            { invoiceRowVersion = invoiceVersion, reason = "Not required after partial collection" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/payments", new
            {
                amount = 100m, method = "Cash", reference = "CASH-INVOICE-1001",
                receivedOn = DateOnly.FromDateTime(DateTime.UtcNow), invoiceRowVersion = invoiceVersion
            })).StatusCode);

        detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{invoiceId}");
        Assert.Equal("Paid", detail.GetProperty("invoice").GetProperty("status").GetString());
        Assert.Equal(2, detail.GetProperty("payments").GetArrayLength());
        var ledger = await admin.GetFromJsonAsync<JsonElement>($"{Root}/ledger?contractId={contractId}");
        Assert.Equal(300m, ledger.GetProperty("collected").GetDecimal());
        Assert.Equal(0m, ledger.GetProperty("outstanding").GetDecimal());
        Assert.Equal(700m, ledger.GetProperty("unbilledAmount").GetDecimal());
        await using var check = fixture.CreateContext();
        Assert.Equal(300m, (await check.SalesContracts.SingleAsync(c => c.Id == contractId)).BilledAmount);
        Assert.Equal(2, await check.SalesPayments.CountAsync());
    }

    [Fact]
    public async Task Invoice_void_releases_unbilled_value_and_concurrent_issue_prevents_overbilling()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var admin = await host.ClientAsync("Admin");
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var (contractId, version) = await SignedContract(fixture, me.GetProperty("id").GetInt64());
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        object Request(decimal amount, string rowVersion) => new
        {
            contractId, contractRowVersion = rowVersion, type = "Milestone",
            description = "Milestone software development", amount, dueDate
        };
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync(Root + "/invoices", Request(1001m, version))).StatusCode);
        var created = await admin.PostAsJsonAsync(Root + "/invoices", Request(400m, version));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{id}");
        var currentContractVersion = detail.GetProperty("contract").GetProperty("rowVersion").GetString()!;
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync(Root + "/invoices", Request(700m, currentContractVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(Root + "/invoices", Request(100m, "invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{id}/void", new
            {
                invoiceRowVersion = detail.GetProperty("invoice").GetProperty("rowVersion").GetString(),
                reason = "Milestone rescheduled by the customer"
            })).StatusCode);

        var ledger = await admin.GetFromJsonAsync<JsonElement>($"{Root}/ledger?contractId={contractId}");
        Assert.Equal(1000m, ledger.GetProperty("unbilledAmount").GetDecimal());
        Assert.Equal(0m, ledger.GetProperty("outstanding").GetDecimal());
        detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{id}");
        Assert.Equal("Void", detail.GetProperty("invoice").GetProperty("status").GetString());
        currentContractVersion = detail.GetProperty("contract").GetProperty("rowVersion").GetString()!;
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync(Root + "/invoices", Request(1000m, currentContractVersion))).StatusCode);
    }
}
