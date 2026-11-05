using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class BillingAdjustmentTests
{
    private const string Root = "/api/admin/billing";

    [Fact]
    public async Task Credit_note_and_refund_preserve_original_invoice_and_payment_evidence()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        var userId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        var (contractId, contractVersion) = await BillingTests.SignedContract(database, userId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var create = await admin.PostAsJsonAsync(Root + "/invoices", new
        {
            contractId, contractRowVersion = contractVersion, type = "Advance",
            description = "Advance invoice for the project", amount = 400m, dueDate = date.AddDays(15)
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var invoiceId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{invoiceId}");
        var invoiceVersion = detail.GetProperty("invoice").GetProperty("rowVersion").GetString();
        var receipt = await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/payments", new
        {
            amount = 300m, method = "BankTransfer", reference = "BILL-ADJUST-RECEIPT-1",
            receivedOn = date, invoiceRowVersion = invoiceVersion
        });
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var paymentId = (await receipt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{invoiceId}");
        invoiceVersion = detail.GetProperty("invoice").GetProperty("rowVersion").GetString();

        var credit = new
        {
            amount = 200m, reason = "Agreed reduction in the delivered project scope",
            issuedOn = date, invoiceRowVersion = invoiceVersion
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/credits", credit)).StatusCode);
        var result = await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/credits", credit);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var payload = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RefundDue", payload.GetProperty("status").GetString());
        Assert.Equal(100m, payload.GetProperty("refundDue").GetDecimal());

        detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{invoiceId}");
        invoiceVersion = detail.GetProperty("invoice").GetProperty("rowVersion").GetString();
        Assert.Equal(400m, detail.GetProperty("invoice").GetProperty("amount").GetDecimal());
        Assert.Equal(200m, detail.GetProperty("invoice").GetProperty("creditedAmount").GetDecimal());
        Assert.Equal(300m, detail.GetProperty("invoice").GetProperty("paidAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/credits", new
            {
                amount = 300m, reason = "Another credit outside the invoice balance",
                issuedOn = date, invoiceRowVersion = invoiceVersion
            })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/refunds", new
            {
                paymentId, amount = 101m, method = "BankTransfer", reference = "ADJUST-REFUND-OVER",
                reason = "The client refund has been transferred",
                refundedOn = date, invoiceRowVersion = invoiceVersion
            })).StatusCode);

        var part = await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/refunds", new
        {
            paymentId, amount = 40m, method = "BankTransfer", reference = "ADJUST-REFUND-001",
            reason = "First partial repayment of the agreed credit",
            refundedOn = date, invoiceRowVersion = invoiceVersion
        });
        Assert.Equal(HttpStatusCode.OK, part.StatusCode);
        detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{invoiceId}");
        invoiceVersion = detail.GetProperty("invoice").GetProperty("rowVersion").GetString();

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/refunds", new
            {
                paymentId, amount = 10m, method = "BankTransfer", reference = "ADJUST-REFUND-001",
                reason = "An accidental duplicate external refund reference",
                refundedOn = date, invoiceRowVersion = invoiceVersion
            })).StatusCode);

        var finish = await admin.PostAsJsonAsync($"{Root}/invoices/{invoiceId}/refunds", new
        {
            paymentId, amount = 60m, method = "BankTransfer", reference = "ADJUST-REFUND-002",
            reason = "Final part of the agreed client repayment",
            refundedOn = date, invoiceRowVersion = invoiceVersion
        });
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{invoiceId}");
        var finalInvoice = detail.GetProperty("invoice");
        Assert.Equal("Paid", finalInvoice.GetProperty("status").GetString());
        Assert.Equal(0m, finalInvoice.GetProperty("outstanding").GetDecimal());
        Assert.Equal(0m, finalInvoice.GetProperty("refundDue").GetDecimal());
        Assert.Equal(200m, finalInvoice.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(100m, finalInvoice.GetProperty("refundedAmount").GetDecimal());
        Assert.Equal(1, detail.GetProperty("credits").GetArrayLength());
        Assert.Equal(2, detail.GetProperty("refunds").GetArrayLength());
        Assert.Equal(100m, detail.GetProperty("payments")[0].GetProperty("refundedAmount").GetDecimal());
        var ledger = await admin.GetFromJsonAsync<JsonElement>($"{Root}/ledger?contractId={contractId}");
        Assert.Equal(800m, ledger.GetProperty("unbilledAmount").GetDecimal());
        Assert.Equal(200m, ledger.GetProperty("collected").GetDecimal());
        Assert.Equal(200m, ledger.GetProperty("credited").GetDecimal());
        Assert.Equal(100m, ledger.GetProperty("refunded").GetDecimal());

        await using var check = database.CreateContext();
        Assert.Equal(400m, (await check.SalesInvoices.SingleAsync(x => x.Id == invoiceId)).Amount);
        Assert.Equal(300m, (await check.SalesPayments.SingleAsync(x => x.Id == paymentId)).Amount);
        Assert.Equal(2, await check.SalesRefunds.CountAsync(x => x.InvoiceId == invoiceId));
        var collection = await admin.GetFromJsonAsync<JsonElement>(
            Root + $"/reports/collections?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        var totals = collection.GetProperty("summary")[0];
        Assert.Equal(300m, totals.GetProperty("collected").GetDecimal());
        Assert.Equal(100m, totals.GetProperty("refunded").GetDecimal());
        Assert.Equal(200m, totals.GetProperty("netCollected").GetDecimal());
    }

    [Fact]
    public async Task Customer_ledger_aggregates_only_customer_linked_invoices()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");
        var userId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        var (contractId, version) = await BillingTests.SignedContract(database, userId);
        long customerId;
        await using (var db = database.CreateContext())
        {
            var customer = new Customer { Name = "Linked ledger company", LegalName = "Ledger Company Ltd" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            var quoteId = (await db.SalesContracts.SingleAsync(x => x.Id == contractId)).QuotationId;
            var leadId = (await db.SalesQuotations.SingleAsync(x => x.Id == quoteId)).LeadId;
            var lead = await db.Leads.SingleAsync(x => x.Id == leadId);
            lead.CustomerId = customerId;
            await db.SaveChangesAsync();
        }
        var invoice = await admin.PostAsJsonAsync(Root + "/invoices", new
        {
            contractId, contractRowVersion = version, type = "Milestone",
            description = "Linked customer project billing", amount = 250m,
            dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
        });
        Assert.Equal(HttpStatusCode.Created, invoice.StatusCode);
        var invoiceId = (await invoice.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.GetAsync($"{Root}/customers/{customerId}/ledger")).StatusCode);
        var ledger = await admin.GetFromJsonAsync<JsonElement>($"{Root}/customers/{customerId}/ledger");
        Assert.Equal(1, ledger.GetProperty("total").GetInt32());
        Assert.Equal("INR", ledger.GetProperty("summary")[0].GetProperty("currency").GetString());
        Assert.Equal(250m, ledger.GetProperty("summary")[0].GetProperty("outstanding").GetDecimal());
        Assert.Equal(invoiceId, ledger.GetProperty("items")[0].GetProperty("id").GetInt64());
        var filtered = await admin.GetFromJsonAsync<JsonElement>(
            $"{Root}/customers/{customerId}/ledger?currency=KWD");
        Assert.Equal(0, filtered.GetProperty("total").GetInt32());
        var search = await admin.GetFromJsonAsync<JsonElement>(
            Root + "/customers?search=Linked%20ledger");
        Assert.Contains(search.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetInt64() == customerId);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync($"{Root}/customers/{customerId}/ledger?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task Financial_approval_rejects_stale_invoice_and_allows_separate_reviewer_to_reject()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        using var reviewer = await host.ClientAsync("Admin");
        var userId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetInt64();
        var (contractId, version) = await BillingTests.SignedContract(database, userId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var issue = await admin.PostAsJsonAsync(Root + "/invoices", new
        {
            contractId, contractRowVersion = version, type = "Milestone",
            description = "Stage billing for services", amount = 200m, dueDate = date.AddDays(10)
        });
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
        var id = (await issue.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{id}");
        var rowVersion = detail.GetProperty("invoice").GetProperty("rowVersion").GetString();
        var submitted = await admin.PostAsJsonAsync($"{Root}/invoices/{id}/credits", new
        {
            amount = 50m, reason = "Agreed discount after original project quotation",
            issuedOn = date, invoiceRowVersion = rowVersion
        });
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        var approvalId = (await submitted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Root}/invoices/{id}/credits", new
            {
                amount = 40m, reason = "Duplicate request against the same billing record",
                issuedOn = date, invoiceRowVersion = rowVersion
            })).StatusCode);

        var pending = await reviewer.GetFromJsonAsync<JsonElement>(Root + "/adjustments");
        var approvalVersion = pending.GetProperty("items")[0].GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await reviewer.PostAsJsonAsync($"{Root}/adjustments/{approvalId}/decision", new
            {
                approve = true, comment = "Verified approval with different administrator",
                rowVersion = "bad"
            })).StatusCode);

        var payment = await admin.PostAsJsonAsync($"{Root}/invoices/{id}/payments", new
        {
            amount = 20m, method = "Cash", reference = "STALE-CREDIT-PAYMENT-001",
            receivedOn = date, invoiceRowVersion = rowVersion
        });
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await reviewer.PostAsJsonAsync($"{Root}/adjustments/{approvalId}/decision", new
            {
                approve = true, comment = "Would be stale after collection",
                rowVersion = approvalVersion
            })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await reviewer.PostAsJsonAsync($"{Root}/adjustments/{approvalId}/decision", new
            {
                approve = false, comment = "Invoice changed. Request must be reviewed again.",
                rowVersion = approvalVersion
            })).StatusCode);

        detail = await admin.GetFromJsonAsync<JsonElement>($"{Root}/invoices/{id}");
        Assert.Equal(0m, detail.GetProperty("invoice").GetProperty("creditedAmount").GetDecimal());
        Assert.Equal(20m, detail.GetProperty("invoice").GetProperty("paidAmount").GetDecimal());
        Assert.Equal(0, detail.GetProperty("credits").GetArrayLength());
        var approved = await reviewer.GetFromJsonAsync<JsonElement>(
            Root + "/adjustments?state=Rejected");
        Assert.Equal(1, approved.GetProperty("total").GetInt32());
    }
}
