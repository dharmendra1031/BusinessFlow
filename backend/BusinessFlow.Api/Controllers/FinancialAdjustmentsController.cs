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
[Route("api/admin/billing/adjustments")]
public sealed class FinancialAdjustmentsController(AppDbContext db) : ControllerBase
{
    private void Audit(string action, FinancialAdjustmentApproval request, long actor) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor, Action = action, TableName = "SalesInvoices",
            RecordId = request.InvoiceId,
            ChangedFields = $"ApprovalId: {request.Id}; Kind: {request.Kind}; Amount: {request.Amount}; " +
                $"Comment: {request.DecisionComment}"
        });

    [HttpGet]
    public async Task<IActionResult> List(string state = "Pending", int page = 1,
        int pageSize = 20, CancellationToken ct = default)
    {
        if (state is not ("Pending" or "Approved" or "Rejected") ||
            page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { message = "Invalid approval filters." });
        var actor = LeadAccess.UserId(User);
        var query = db.FinancialAdjustmentApprovals.AsNoTracking()
            .Where(x => x.State == state);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.InvoiceId, x.Kind, x.Amount, x.PaymentId,
                x.Method, x.Reference, x.Reason, x.EffectiveOn, x.State,
                x.RequestedById, x.RequestedAtUtc, x.DecidedById, x.DecidedAtUtc,
                x.DecisionComment, x.RowVersion
            }).ToListAsync(ct);
        return Ok(new
        {
            total, page, pageSize,
            items = items.Select(x => new
            {
                x.Id, x.InvoiceId, x.Kind, x.Amount, x.PaymentId,
                x.Method, x.Reference, x.Reason, x.EffectiveOn, x.State,
                x.RequestedById, x.RequestedAtUtc, x.DecidedById, x.DecidedAtUtc,
                x.DecisionComment, x.RowVersion,
                canDecide = x.State == "Pending" && x.RequestedById != actor
            })
        });
    }

    [HttpGet("invoices/{invoiceId:long}")]
    public async Task<IActionResult> InvoiceHistory(long invoiceId, CancellationToken ct)
    {
        if (!await db.SalesInvoices.AsNoTracking().AnyAsync(x => x.Id == invoiceId, ct))
            return NotFound();
        var items = await db.FinancialAdjustmentApprovals.AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId).OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id, x.Kind, x.Amount, x.State, x.Reason,
                x.RequestedById, x.RequestedAtUtc,
                x.DecidedById, x.DecidedAtUtc, x.DecisionComment
            }).Take(100).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("{id:long}/decision")]
    public async Task<IActionResult> Decide(long id, FinancialDecisionRequest decision,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(decision.Comment) ||
            decision.Comment.Trim().Length is < 10 or > 500)
            return BadRequest(new { message = "Review comment must be 10 to 500 characters." });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var request = await db.FinancialAdjustmentApprovals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return NotFound();
        if (request.State != "Pending")
            return Conflict(new { message = "This adjustment has already been reviewed." });
        var actor = LeadAccess.UserId(User);
        if (actor == request.RequestedById)
            return Conflict(new { message = "A different Admin must review this request." });
        if (!ConcurrencyTokens.Apply(db, request, decision.RowVersion))
            return BadRequest(new { message = "Reload the approval before deciding." });

        if (decision.Approve)
        {
            var invoice = await db.SalesInvoices.FirstOrDefaultAsync(
                x => x.Id == request.InvoiceId, ct);
            if (invoice is null) return NotFound();
            if (!invoice.RowVersion.SequenceEqual(request.InvoiceVersion))
                return Conflict(new { message = "Invoice changed after submission. Reject and resubmit this request." });
            if (request.Kind == "Credit")
            {
                if (invoice.Status == "Void" ||
                    request.Amount > invoice.Amount - invoice.CreditedAmount)
                    return Conflict(new { message = "Credit exceeds the remaining invoice amount." });
                var contract = await db.SalesContracts.FirstAsync(
                    x => x.Id == invoice.ContractId, ct);
                if (contract.BilledAmount < request.Amount)
                    return Conflict(new { message = "Contract billing balance is inconsistent." });
                contract.BilledAmount -= request.Amount;
                invoice.CreditedAmount += request.Amount;
                db.SalesCreditNotes.Add(new SalesCreditNote
                {
                    InvoiceId = invoice.Id, Amount = request.Amount,
                    Reason = request.Reason, IssuedOn = request.EffectiveOn,
                    CreatedById = request.RequestedById
                });
            }
            else
            {
                var payment = await db.SalesPayments.FirstOrDefaultAsync(
                    x => x.Id == request.PaymentId && x.InvoiceId == invoice.Id, ct);
                if (payment is null) return Conflict(new { message = "Original payment is missing." });
                if (invoice.Status != "RefundDue" ||
                    request.Amount > BillingBalance.RefundDue(invoice) ||
                    request.Amount > payment.Amount - payment.RefundedAmount)
                    return Conflict(new { message = "Refund exceeds the remaining refundable amount." });
                if (await db.SalesRefunds.AnyAsync(x => x.Reference == request.Reference, ct) ||
                    await db.SalesPayments.AnyAsync(x => x.Reference == request.Reference, ct))
                    return Conflict(new { message = "The refund reference is already recorded." });

                payment.RefundedAmount += request.Amount;
                invoice.PaidAmount -= request.Amount;
                invoice.RefundedAmount += request.Amount;
                db.SalesRefunds.Add(new SalesRefund
                {
                    InvoiceId = invoice.Id, PaymentId = payment.Id,
                    Amount = request.Amount, Method = request.Method!,
                    Reference = request.Reference!, Reason = request.Reason,
                    RefundedOn = request.EffectiveOn, CreatedById = request.RequestedById
                });
            }
            invoice.Status = BillingBalance.Status(invoice);
            invoice.UpdatedAtUtc = DateTime.UtcNow;
        }

        request.State = decision.Approve ? "Approved" : "Rejected";
        request.DecidedById = actor;
        request.DecidedAtUtc = DateTime.UtcNow;
        request.DecisionComment = decision.Comment.Trim();
        Audit(request.State == "Approved" ? "AdjustmentApproved" : "AdjustmentRejected", request, actor);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Adjustment changed. Reload before reviewing." });
        }
        catch (DbUpdateException error) when (
            error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "Adjustment conflicts with an existing financial record." });
        }
    }
}
