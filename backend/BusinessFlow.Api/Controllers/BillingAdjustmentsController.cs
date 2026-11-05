using System.Data;
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

    private void Audit(string action, long invoiceId, long approvalId) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User),
            Action = action, TableName = "SalesInvoices", RecordId = invoiceId,
            ChangedFields = "AdjustmentApprovalId: " + approvalId
        });

    private async Task<IActionResult> Submit(FinancialAdjustmentApproval approval,
        string version, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Id == approval.InvoiceId, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status == "Void") return Conflict(new { message = "Voided invoices cannot be adjusted." });
        if (!ConcurrencyTokens.Apply(db, invoice, version))
            return BadRequest(new { message = "Reload the invoice before submitting an adjustment." });
        if (await db.FinancialAdjustmentApprovals.AnyAsync(x =>
            x.InvoiceId == invoice.Id && x.Kind == approval.Kind && x.State == "Pending", ct))
            return Conflict(new { message = "A request for this adjustment type is already pending." });

        if (approval.Kind == "Credit")
        {
            if (approval.Amount > invoice.Amount - invoice.CreditedAmount)
                return Conflict(new { message = "Credit exceeds the remaining invoice value." });
            var contract = await db.SalesContracts.AsNoTracking().FirstAsync(
                x => x.Id == invoice.ContractId, ct);
            if (contract.BilledAmount < approval.Amount)
                return Conflict(new { message = "Contract billed balance is inconsistent." });
        }
        else
        {
            var payment = await db.SalesPayments.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == approval.PaymentId && x.InvoiceId == invoice.Id, ct);
            if (payment is null) return NotFound(new { message = "The payment is not part of this invoice." });
            if (invoice.Status != "RefundDue" ||
                approval.Amount > BillingBalance.RefundDue(invoice) ||
                approval.Amount > payment.Amount - payment.RefundedAmount)
                return Conflict(new { message = "Requested refund exceeds the refundable balance." });
            if (await db.SalesRefunds.AnyAsync(x => x.Reference == approval.Reference, ct) ||
                await db.SalesPayments.AnyAsync(x => x.Reference == approval.Reference, ct) ||
                await db.FinancialAdjustmentApprovals.AnyAsync(x =>
                    x.Kind == "Refund" && x.State == "Pending" && x.Reference == approval.Reference, ct))
                return Conflict(new { message = "The refund reference has already been used." });
        }

        approval.InvoiceVersion = invoice.RowVersion.ToArray();
        approval.RequestedById = LeadAccess.UserId(User);
        db.FinancialAdjustmentApprovals.Add(approval);
        try
        {
            await db.SaveChangesAsync(ct);
            Audit("AdjustmentSubmitted", invoice.Id, approval.Id);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Accepted(new { approval.Id, approval.State });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "A duplicate pending adjustment exists." });
        }
    }

    [HttpPost("credits")]
    public Task<IActionResult> IssueCreditNote(long invoiceId, IssueCreditNoteRequest request,
        CancellationToken ct)
    {
        if (!ValidAmount(request.Amount) || !ValidReason(request.Reason) || !ValidDate(request.IssuedOn))
            return Task.FromResult<IActionResult>(
                BadRequest(new { message = "Enter a valid credit amount, reason and date." }));
        return Submit(new FinancialAdjustmentApproval
        {
            InvoiceId = invoiceId, Kind = "Credit", Amount = request.Amount,
            Reason = request.Reason.Trim(), EffectiveOn = request.IssuedOn
        }, request.InvoiceRowVersion, ct);
    }

    [HttpPost("refunds")]
    public Task<IActionResult> RecordRefund(long invoiceId, RecordRefundRequest request,
        CancellationToken ct)
    {
        if (!ValidAmount(request.Amount) || !ValidReason(request.Reason) ||
            !ValidDate(request.RefundedOn) ||
            request.Method is not ("BankTransfer" or "UPI" or "Cash" or "Card" or "Other") ||
            string.IsNullOrWhiteSpace(request.Reference) ||
            request.Reference.Trim().Length is < 5 or > 100)
            return Task.FromResult<IActionResult>(
                BadRequest(new { message = "Enter valid refund payment, amount, method, reason, date and reference." }));
        return Submit(new FinancialAdjustmentApproval
        {
            InvoiceId = invoiceId, Kind = "Refund", Amount = request.Amount,
            PaymentId = request.PaymentId, Method = request.Method, Reference = request.Reference.Trim(),
            Reason = request.Reason.Trim(), EffectiveOn = request.RefundedOn
        }, request.InvoiceRowVersion, ct);
    }
}
