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
[Route("api/admin/billing")]
public sealed class BillingController(AppDbContext db) : ControllerBase
{
    private const decimal MaxAmount = 999999999999999.999m;

    private static bool ValidAmount(decimal amount) =>
        amount > 0 && amount <= MaxAmount && decimal.Round(amount, 3) == amount;

    private void Audit(string action, string table, long id, string details) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = action,
            TableName = table, RecordId = id, ChangedFields = details
        });

    [HttpGet("contracts")]
    public async Task<IActionResult> Contracts(string? search = null, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!ValidPage(page, pageSize, search)) return BadRequest(new { message = "Invalid filters." });

        var query = from contract in db.SalesContracts.AsNoTracking()
                    join quote in db.SalesQuotations.AsNoTracking() on contract.QuotationId equals quote.Id
                    join revision in db.SalesQuotationRevisions.AsNoTracking()
                        on new { QuotationId = quote.Id, Revision = contract.QuotationRevision }
                        equals new { revision.QuotationId, revision.Revision }
                    join lead in db.Leads.AsNoTracking() on quote.LeadId equals lead.Id
                    where contract.Status == "Signed"
                    select new
                    {
                        contract.Id, contract.QuotationId, contract.RowVersion,
                        ClientName = lead.CompanyName ?? lead.Name,
                        revision.Title, revision.Currency,
                        ContractAmount = revision.Amount, contract.BilledAmount,
                        UnbilledAmount = revision.Amount - contract.BilledAmount
                    };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.ClientName.Contains(term) || x.Title.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> List(string? search = null, string? status = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!ValidPage(page, pageSize, search) ||
            status is not (null or "Issued" or "PartiallyPaid" or "Paid" or "Void"))
            return BadRequest(new { message = "Invalid invoice filters." });

        var query = db.SalesInvoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.BillToName.Contains(term) || x.Description.Contains(term));
        }
        if (status is not null) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.ContractId, x.Type, x.Description, x.BillToName, x.Currency,
                x.Amount, x.PaidAmount, Outstanding = x.Status == "Void" ? 0 : x.Amount - x.PaidAmount,
                x.DueDate, x.Status, x.CreatedAtUtc
            }).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("invoices/{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct = default)
    {
        var invoice = await db.SalesInvoices.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.ContractId, x.Type, x.Description, x.BillToName, x.Currency,
                x.Amount, x.PaidAmount, Outstanding = x.Status == "Void" ? 0 : x.Amount - x.PaidAmount,
                x.DueDate, x.Status, x.VoidReason, x.VoidedAtUtc, x.RowVersion,
                x.CreatedAtUtc, x.UpdatedAtUtc
            }).FirstOrDefaultAsync(ct);
        if (invoice is null) return NotFound();
        var contract = await db.SalesContracts.AsNoTracking().Where(x => x.Id == invoice.ContractId)
            .Select(x => new { x.Id, x.QuotationId, x.QuotationRevision, x.BilledAmount, x.RowVersion })
            .FirstAsync(ct);
        var payments = await db.SalesPayments.AsNoTracking().Where(x => x.InvoiceId == id)
            .OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Amount, x.Method, x.Reference, x.ReceivedOn, x.CreatedAtUtc })
            .ToListAsync(ct);
        return Ok(new { invoice, contract, payments });
    }

    [HttpPost("invoices")]
    public async Task<IActionResult> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        if (request.Type is not ("Advance" or "Milestone" or "Final") ||
            string.IsNullOrWhiteSpace(request.Description) ||
            request.Description.Trim().Length is < 3 or > 250 || !ValidAmount(request.Amount) ||
            request.DueDate == default)
            return BadRequest(new { message = "Enter a valid invoice type, description, amount and due date." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var contract = await db.SalesContracts.FirstOrDefaultAsync(x => x.Id == request.ContractId, ct);
        if (contract is null) return NotFound();
        if (contract.Status != "Signed") return Conflict(new { message = "The contract must be signed before billing." });
        if (!ConcurrencyTokens.Apply(db, contract, request.ContractRowVersion))
            return BadRequest(new { message = "Reload the signed contract before billing." });
        var revision = await db.SalesQuotationRevisions.AsNoTracking()
            .Where(x => x.QuotationId == contract.QuotationId && x.Revision == contract.QuotationRevision)
            .Select(x => new { x.Amount, x.Currency }).FirstOrDefaultAsync(ct);
        if (revision is null) return Conflict(new { message = "The accepted contract quotation is missing." });
        var available = revision.Amount - contract.BilledAmount;
        if (request.Amount > available)
            return Conflict(new { message = "Invoice would exceed the unbilled signed contract value." });

        var billTo = await (from quote in db.SalesQuotations.AsNoTracking()
            join lead in db.Leads.AsNoTracking() on quote.LeadId equals lead.Id
            join customer in db.Customers.AsNoTracking() on lead.CustomerId equals customer.Id into clients
            from customer in clients.DefaultIfEmpty()
            where quote.Id == contract.QuotationId
            select customer == null ? (lead.CompanyName ?? lead.Name) : (customer.LegalName ?? customer.Name))
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(billTo))
            return Conflict(new { message = "The contract customer is not available." });

        contract.BilledAmount += request.Amount;
        var invoice = new SalesInvoice
        {
            ContractId = contract.Id, Type = request.Type, Description = request.Description.Trim(),
            Amount = request.Amount, Currency = revision.Currency, BillToName = billTo,
            DueDate = request.DueDate, CreatedById = LeadAccess.UserId(User)
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        Audit("Issue", "SalesInvoices", invoice.Id, "Contract: " + contract.Id + "; Amount: " + invoice.Amount);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = invoice.Id }, new { invoice.Id });
    }

    [HttpPost("invoices/{id:long}/payments")]
    public async Task<IActionResult> Receive(long id, ReceiveInvoicePaymentRequest request, CancellationToken ct)
    {
        if (!ValidAmount(request.Amount) ||
            request.Method is not ("BankTransfer" or "UPI" or "Cash" or "Card" or "Other") ||
            string.IsNullOrWhiteSpace(request.Reference) ||
            request.Reference.Trim().Length is < 5 or > 100 ||
            request.ReceivedOn == default || request.ReceivedOn > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return BadRequest(new { message = "Enter a valid amount, payment method, unique reference and received date." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status is "Paid" or "Void")
            return Conflict(new { message = "A paid or void invoice cannot accept payments." });
        if (!ConcurrencyTokens.Apply(db, invoice, request.InvoiceRowVersion))
            return BadRequest(new { message = "Reload the invoice before receiving payment." });
        if (request.Amount > invoice.Amount - invoice.PaidAmount)
            return Conflict(new { message = "Payment exceeds the invoice outstanding balance." });

        var reference = request.Reference.Trim();
        if (await db.SalesPayments.AnyAsync(x => x.Reference == reference, ct))
            return Conflict(new { message = "This payment reference is already recorded." });

        invoice.PaidAmount += request.Amount;
        invoice.Status = invoice.PaidAmount == invoice.Amount ? "Paid" : "PartiallyPaid";
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        var payment = new SalesPayment
        {
            InvoiceId = id, Amount = request.Amount, Method = request.Method,
            Reference = reference, ReceivedOn = request.ReceivedOn, CreatedById = LeadAccess.UserId(User)
        };
        db.SalesPayments.Add(payment);
        try
        {
            await db.SaveChangesAsync(ct);
            Audit("Receive", "SalesInvoices", id, "PaymentId: " + payment.Id + "; Amount: " + request.Amount);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(new { payment.Id, invoice.Status, invoice.PaidAmount, outstanding = invoice.Amount - invoice.PaidAmount });
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "This payment reference is already recorded." });
        }
    }

    [HttpPost("invoices/{id:long}/void")]
    public async Task<IActionResult> Void(long id, VoidInvoiceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 500)
            return BadRequest(new { message = "Void reason must be 10 to 500 characters." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status != "Issued" || invoice.PaidAmount != 0)
            return Conflict(new { message = "Only unpaid issued invoices can be voided." });
        if (!ConcurrencyTokens.Apply(db, invoice, request.InvoiceRowVersion))
            return BadRequest(new { message = "Reload the invoice before voiding." });
        var contract = await db.SalesContracts.FirstAsync(x => x.Id == invoice.ContractId, ct);
        if (contract.BilledAmount < invoice.Amount)
            return Conflict(new { message = "Contract billing balance is inconsistent." });

        contract.BilledAmount -= invoice.Amount;
        invoice.Status = "Void";
        invoice.VoidedAtUtc = DateTime.UtcNow;
        invoice.VoidReason = request.Reason.Trim();
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        Audit("Void", "SalesInvoices", id, request.Reason.Trim());
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger(long contractId, CancellationToken ct = default)
    {
        if (contractId <= 0) return BadRequest(new { message = "Select a contract." });
        var contract = await db.SalesContracts.AsNoTracking().Where(x => x.Id == contractId)
            .Select(x => new { x.Id, x.QuotationId, x.QuotationRevision, x.Status, x.BilledAmount })
            .FirstOrDefaultAsync(ct);
        if (contract is null) return NotFound();
        var revision = await db.SalesQuotationRevisions.AsNoTracking()
            .Where(x => x.QuotationId == contract.QuotationId && x.Revision == contract.QuotationRevision)
            .Select(x => new { x.Amount, x.Currency }).FirstOrDefaultAsync(ct);
        if (revision is null) return Conflict(new { message = "Quotation revision missing." });
        var invoices = await db.SalesInvoices.AsNoTracking().Where(x => x.ContractId == contractId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.Type, x.Description, x.Status, x.Amount, x.PaidAmount,
                Outstanding = x.Status == "Void" ? 0 : x.Amount - x.PaidAmount,
                x.CreatedAtUtc
            }).ToListAsync(ct);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var payments = await db.SalesPayments.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId))
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.InvoiceId, x.Amount, x.Method, x.Reference, x.ReceivedOn })
            .ToListAsync(ct);
        return Ok(new
        {
            contract, revision.Currency, ContractAmount = revision.Amount,
            UnbilledAmount = revision.Amount - contract.BilledAmount,
            Outstanding = invoices.Sum(x => x.Outstanding),
            Collected = invoices.Sum(x => x.PaidAmount),
            invoices, payments
        });
    }

    private static bool ValidPage(int page, int pageSize, string? search) =>
        page > 0 && pageSize is >= 1 and <= 100 &&
        (long)(page - 1) * pageSize <= int.MaxValue && search?.Length is not > 150;
}
