using BusinessFlow.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/billing/customers")]
public sealed class CustomerLedgerController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(string? search = null, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!ValidPage(page, pageSize) || search?.Length is > 150)
            return BadRequest(new { message = "Invalid customer filters." });

        var query = db.Customers.AsNoTracking().Where(x => x.MergedIntoCustomerId == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.Contains(term) ||
                x.LegalName != null && x.LegalName.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Name, x.LegalName })
            .ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:long}/ledger")]
    public async Task<IActionResult> Ledger(long id, string? currency = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (id <= 0 || !ValidPage(page, pageSize) ||
            currency is { Length: > 0 } && (currency.Length != 3 ||
                !currency.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')))
            return BadRequest(new { message = "Invalid ledger filters." });

        var customer = await db.Customers.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Name, x.LegalName, x.MergedIntoCustomerId })
            .FirstOrDefaultAsync(ct);
        if (customer is null) return NotFound();

        var query = from invoice in db.SalesInvoices.AsNoTracking()
                    join contract in db.SalesContracts.AsNoTracking() on invoice.ContractId equals contract.Id
                    join quote in db.SalesQuotations.AsNoTracking() on contract.QuotationId equals quote.Id
                    join lead in db.Leads.AsNoTracking() on quote.LeadId equals lead.Id
                    where lead.CustomerId == id
                    select invoice;
        if (!string.IsNullOrWhiteSpace(currency))
        {
            var code = currency.ToUpperInvariant();
            query = query.Where(x => x.Currency == code);
        }

        var active = query.Where(x => x.Status != "Void");
        var summary = await active.GroupBy(x => x.Currency).Select(group => new
        {
            currency = group.Key,
            invoiceCount = group.Count(),
            invoiced = group.Sum(x => x.Amount - x.CreditedAmount),
            collected = group.Sum(x => x.PaidAmount),
            credited = group.Sum(x => x.CreditedAmount),
            refunded = group.Sum(x => x.RefundedAmount),
            outstanding = group.Sum(x => x.Amount - x.CreditedAmount > x.PaidAmount ?
                x.Amount - x.CreditedAmount - x.PaidAmount : 0m),
            refundDue = group.Sum(x => x.PaidAmount > x.Amount - x.CreditedAmount ?
                x.PaidAmount - (x.Amount - x.CreditedAmount) : 0m)
        }).OrderBy(x => x.currency).ToListAsync(ct);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.ContractId, x.Type, x.Description, x.Currency, x.Status,
                x.DueDate, x.Amount, x.CreditedAmount, x.PaidAmount, x.RefundedAmount,
                outstanding = x.Status == "Void" ? 0m :
                    x.Amount - x.CreditedAmount > x.PaidAmount ?
                    x.Amount - x.CreditedAmount - x.PaidAmount : 0m,
                refundDue = x.PaidAmount > x.Amount - x.CreditedAmount ?
                    x.PaidAmount - (x.Amount - x.CreditedAmount) : 0m
            }).ToListAsync(ct);
        return Ok(new { customer, total, page, pageSize, summary, items });
    }

    private static bool ValidPage(int page, int pageSize) =>
        page > 0 && pageSize is >= 1 and <= 100 &&
        (long)(page - 1) * pageSize <= int.MaxValue;
}
