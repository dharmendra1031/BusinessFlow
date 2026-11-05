using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/billing/invoices/{invoiceId:long}")]
public sealed class BillingAdjustmentsController(AppDbContext db) : ControllerBase
{
    private static bool ValidAmount(decimal amount) =>
        amount > 0 && amount <= 999999999999999.999m && decimal.Round(amount, 3) == amount;

    private static bool ValidDate(DateOnly date) =>
        date != default && date <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    private static bool ValidReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length is >= 10 and <= 500;

    private void Audit(string action, long invoiceId, string details) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User),
            Action = action, TableName = "SalesInvoices",
            RecordId = invoiceId, ChangedFields = details
        });

    [HttpPost("credits")]
    public async Task<IActionResult> IssueCreditNote(long invoiceId, IssueCreditNoteRequest request,
        CancellationToken ct)
    {
        if (!ValidAmount(request.Amount) || !ValidReason(request.Reason) || !ValidDate(request.IssuedOn))
            return BadRequest(new { message = "Enter a valid credit amount, reason and issue date." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status == "Void")
            return Conflict(new { message = "Voided invoices cannot receive credit notes." });
        if (!ConcurrencyTokens.Apply(db, invoice, request.InvoiceRowVersion))
            return BadRequest(new { message = "Reload this invoice before issuing a credit note." });

        if (request.Amount > invoice.Amount - invoice.CreditedAmount)
            return Conflict(new { message = "Credit exceeds the remaining invoice value." });

        var contract = await db.SalesContracts.FirstAsync(x => x.Id == invoice.ContractId, ct);
        if (contract.BilledAmount < request.Amount)
            return Conflict(new { message = "Contract billing balance is inconsistent." });

        contract.BilledAmount -= request.Amount;
        invoice.CreditedAmount += request.Amount;
        invoice.Status = BillingBalance.Status(invoice);
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        var credit = new SalesCreditNote
        {
            InvoiceId = invoiceId, Amount = request.Amount,
            Reason = request.Reason.Trim(), IssuedOn = request.IssuedOn,
            CreatedById = LeadAccess.UserId(User)
        };
        db.SalesCreditNotes.Add(credit);
        await db.SaveChangesAsync(ct);
        Audit("CreditNote", invoiceId, "CreditNoteId: " + credit.Id + "; Amount: " + credit.Amount);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Ok(new
        {
            credit.Id, invoice.Status, invoice.CreditedAmount,
            outstanding = BillingBalance.Outstanding(invoice),
            refundDue = BillingBalance.RefundDue(invoice)
        });
    }

    [HttpPost("refunds")]
    public async Task<IActionResult> RecordRefund(long invoiceId, RecordRefundRequest request,
        CancellationToken ct)
    {
        if (!ValidAmount(request.Amount) || !ValidReason(request.Reason) ||
            !ValidDate(request.RefundedOn) ||
            request.Method is not ("BankTransfer" or "UPI" or "Cash" or "Card" or "Other") ||
            string.IsNullOrWhiteSpace(request.Reference) ||
            request.Reference.Trim().Length is < 5 or > 100)
            return BadRequest(new { message = "Enter the payment, refund amount, method, reason, date and unique reference." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (invoice is null) return NotFound();
        if (!ConcurrencyTokens.Apply(db, invoice, request.InvoiceRowVersion))
            return BadRequest(new { message = "Reload this invoice before recording a refund." });

        var payment = await db.SalesPayments.FirstOrDefaultAsync(
            x => x.Id == request.PaymentId && x.InvoiceId == invoiceId, ct);
        if (payment is null) return NotFound(new { message = "The payment does not belong to this invoice." });
        if (invoice.Status != "RefundDue" || request.Amount > BillingBalance.RefundDue(invoice))
            return Conflict(new { message = "Refund cannot exceed the credit-generated refundable balance." });
        if (request.Amount > payment.Amount - payment.RefundedAmount)
            return Conflict(new { message = "Refund cannot exceed the remaining original payment amount." });

        var reference = request.Reference.Trim();
        if (await db.SalesRefunds.AnyAsync(x => x.Reference == reference, ct) ||
            await db.SalesPayments.AnyAsync(x => x.Reference == reference, ct))
            return Conflict(new { message = "The refund reference is already used." });

        payment.RefundedAmount += request.Amount;
        invoice.PaidAmount -= request.Amount;
        invoice.RefundedAmount += request.Amount;
        invoice.Status = BillingBalance.Status(invoice);
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        var refund = new SalesRefund
        {
            InvoiceId = invoiceId, PaymentId = payment.Id,
            Amount = request.Amount, Method = request.Method,
            Reference = reference, Reason = request.Reason.Trim(),
            RefundedOn = request.RefundedOn, CreatedById = LeadAccess.UserId(User)
        };
        db.SalesRefunds.Add(refund);
        try
        {
            await db.SaveChangesAsync(ct);
            Audit("Refund", invoiceId, "RefundId: " + refund.Id + "; PaymentId: " + payment.Id +
                "; Amount: " + refund.Amount + "; Reference: " + reference);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(new
            {
                refund.Id, invoice.Status, invoice.PaidAmount,
                outstanding = BillingBalance.Outstanding(invoice),
                refundDue = BillingBalance.RefundDue(invoice)
            });
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "The refund reference is already recorded." });
        }
    }
}
