using BusinessFlow.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = "ClientPortal")]
[Route("api/client")]
public sealed class ClientDocumentsController(AppDbContext db) : ControllerBase
{
    private const int PageSize = 20;

    private async Task<long?> CustomerIdAsync(CancellationToken ct)
    {
        if (!long.TryParse(User.FindFirst("client_account")?.Value, out var accountId) || accountId <= 0)
            return null;

        return await db.ClientPortalAccounts.AsNoTracking()
            .Where(x => x.Id == accountId && x.IsActive)
            .Select(x => (long?)x.CustomerId).SingleOrDefaultAsync(ct);
    }

    [HttpGet("quotations")]
    public async Task<IActionResult> Quotations(int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000) return BadRequest(new { message = "Invalid page." });
        var customerId = await CustomerIdAsync(ct);
        if (customerId is null) return Unauthorized();

        var query = from quote in db.SalesQuotations.AsNoTracking()
                    join lead in db.Leads.AsNoTracking() on quote.LeadId equals lead.Id
                    join revision in db.SalesQuotationRevisions.AsNoTracking()
                        on new { QuotationId = quote.Id, Revision = quote.CurrentRevision }
                        equals new { revision.QuotationId, revision.Revision }
                    where lead.CustomerId == customerId &&
                          (quote.Status == "Sent" || quote.Status == "Accepted" || quote.Status == "Rejected")
                    select new
                    {
                        quote.Id, quote.Status, quote.CurrentRevision,
                        revision.Title, revision.Amount, revision.Currency,
                        revision.ValidUntil, quote.SentAtUtc
                    };

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize = PageSize });
    }

    [HttpGet("quotations/{id:long}")]
    public async Task<IActionResult> Quotation(long id, CancellationToken ct)
    {
        if (id <= 0) return NotFound();
        var customerId = await CustomerIdAsync(ct);
        if (customerId is null) return Unauthorized();

        var quote = await (from quotation in db.SalesQuotations.AsNoTracking()
                           join lead in db.Leads.AsNoTracking() on quotation.LeadId equals lead.Id
                           join revision in db.SalesQuotationRevisions.AsNoTracking()
                               on new { QuotationId = quotation.Id, Revision = quotation.CurrentRevision }
                               equals new { revision.QuotationId, revision.Revision }
                           where quotation.Id == id && lead.CustomerId == customerId &&
                                 (quotation.Status == "Sent" || quotation.Status == "Accepted" ||
                                  quotation.Status == "Rejected")
                           select new
                           {
                               quotation.Id, quotation.Status, quotation.CurrentRevision,
                               revisionId = revision.Id, revision.Title, revision.Scope,
                               revision.Amount, revision.Subtotal, revision.DiscountAmount,
                               revision.TaxAmount, revision.Currency, revision.ValidUntil,
                               revision.PaymentTerms, quotation.SentAtUtc
                           }).SingleOrDefaultAsync(ct);
        if (quote is null) return NotFound();

        var items = await db.SalesQuotationLineItems.AsNoTracking()
            .Where(x => x.RevisionId == quote.revisionId).OrderBy(x => x.Position)
            .Select(x => new
            {
                x.Description, x.Quantity, x.UnitPrice,
                x.DiscountRate, x.TaxRate, x.TotalAmount
            }).ToListAsync(ct);

        return Ok(new
        {
            quote.Id, quote.Status, quote.CurrentRevision, quote.Title, quote.Scope,
            quote.Amount, quote.Subtotal, quote.DiscountAmount, quote.TaxAmount,
            quote.Currency, quote.ValidUntil, quote.PaymentTerms, quote.SentAtUtc, items
        });
    }

    [HttpGet("projects")]
    public async Task<IActionResult> Projects(int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000) return BadRequest(new { message = "Invalid page." });
        var customerId = await CustomerIdAsync(ct);
        if (customerId is null) return Unauthorized();

        var query = from milestone in db.ProjectMilestones.AsNoTracking()
                    join project in db.DevelopmentProjects.AsNoTracking()
                        on milestone.ProjectId equals project.Id
                    join lead in db.Leads.AsNoTracking() on project.LeadId equals lead.Id
                    where lead.CustomerId == customerId && milestone.PublishedAtUtc != null
                    select new
                    {
                        milestone.Id, milestone.ProjectId, ProjectTitle = project.Title,
                        milestone.Title, milestone.Status, milestone.DueDate,
                        milestone.PublishedAtUtc
                    };

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.PublishedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize = PageSize });
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000) return BadRequest(new { message = "Invalid page." });
        var customerId = await CustomerIdAsync(ct);
        if (customerId is null) return Unauthorized();

        var query = from invoice in db.SalesInvoices.AsNoTracking()
                    join contract in db.SalesContracts.AsNoTracking() on invoice.ContractId equals contract.Id
                    join quote in db.SalesQuotations.AsNoTracking() on contract.QuotationId equals quote.Id
                    join lead in db.Leads.AsNoTracking() on quote.LeadId equals lead.Id
                    where lead.CustomerId == customerId && contract.Status == "Signed" &&
                          quote.Status == "Accepted" &&
                          (invoice.Status == "Issued" || invoice.Status == "PartiallyPaid" ||
                           invoice.Status == "Paid" || invoice.Status == "RefundDue" ||
                           invoice.Status == "Credited")
                    select new
                    {
                        invoice.Id, invoice.Type, invoice.Description, invoice.Currency,
                        invoice.Amount, invoice.PaidAmount, invoice.CreditedAmount,
                        invoice.DueDate, invoice.Status, invoice.CreatedAtUtc,
                        Outstanding = invoice.Amount - invoice.CreditedAmount > invoice.PaidAmount ?
                            invoice.Amount - invoice.CreditedAmount - invoice.PaidAmount : 0m
                    };

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize = PageSize });
    }

    [HttpGet("invoices/{id:long}")]
    public async Task<IActionResult> Invoice(long id, CancellationToken ct)
    {
        if (id <= 0) return NotFound();
        var customerId = await CustomerIdAsync(ct);
        if (customerId is null) return Unauthorized();

        var invoice = await (from entry in db.SalesInvoices.AsNoTracking()
                             join contract in db.SalesContracts.AsNoTracking() on entry.ContractId equals contract.Id
                             join quote in db.SalesQuotations.AsNoTracking() on contract.QuotationId equals quote.Id
                             join lead in db.Leads.AsNoTracking() on quote.LeadId equals lead.Id
                             where entry.Id == id && lead.CustomerId == customerId &&
                                   contract.Status == "Signed" && quote.Status == "Accepted" &&
                                   (entry.Status == "Issued" || entry.Status == "PartiallyPaid" ||
                                    entry.Status == "Paid" || entry.Status == "RefundDue" ||
                                    entry.Status == "Credited")
                             select new
                             {
                                 entry.Id, entry.Type, entry.Description, entry.Currency,
                                 entry.Amount, entry.PaidAmount, entry.CreditedAmount,
                                 entry.RefundedAmount, entry.DueDate, entry.Status,
                                 entry.CreatedAtUtc,
                                 Outstanding = entry.Amount - entry.CreditedAmount > entry.PaidAmount ?
                                     entry.Amount - entry.CreditedAmount - entry.PaidAmount : 0m
                             }).SingleOrDefaultAsync(ct);
        return invoice is null ? NotFound() : Ok(invoice);
    }
}
