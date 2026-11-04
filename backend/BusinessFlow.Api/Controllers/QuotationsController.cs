using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

// Commercial records remain Admin-only until sales permissions are approved.
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/quotations")]
public sealed class QuotationsController(AppDbContext db) : ControllerBase
{
    private static bool Valid(QuotationDetails? details) =>
        details is not null &&
        !string.IsNullOrWhiteSpace(details.Title) && details.Title.Trim().Length is >= 2 and <= 150 &&
        (details.Scope?.Trim().Length ?? 0) <= 2000 &&
        (details.PaymentTerms?.Trim().Length ?? 0) <= 1000 &&
        details.Amount > 0 && decimal.Round(details.Amount, 3) == details.Amount &&
        details.Currency is not null && details.Currency.Length == 3 &&
        details.Currency.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static SalesQuotationRevision Revision(long quotationId, int number,
        QuotationDetails details, long userId) => new()
        {
            QuotationId = quotationId,
            Revision = number,
            Title = details.Title.Trim(),
            Scope = string.IsNullOrWhiteSpace(details.Scope) ? null : details.Scope.Trim(),
            Amount = details.Amount,
            Currency = details.Currency.ToUpperInvariant(),
            ValidUntil = details.ValidUntil,
            PaymentTerms = string.IsNullOrWhiteSpace(details.PaymentTerms) ? null : details.PaymentTerms.Trim(),
            CreatedById = userId
        };

    private void Audit(string action, long quotationId, string fields) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = action,
            TableName = "SalesQuotations", RecordId = quotationId, ChangedFields = fields
        });

    [HttpGet]
    public async Task<IActionResult> List(string? search = null, int page = 1,
        int pageSize = 30, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Length > 150)
            return BadRequest(new { message = "Invalid quotation filters." });

        var query = from quote in db.SalesQuotations.AsNoTracking()
                    join lead in db.Leads.AsNoTracking() on quote.LeadId equals lead.Id
                    join revision in db.SalesQuotationRevisions.AsNoTracking()
                        on new { QuotationId = quote.Id, Revision = quote.CurrentRevision }
                        equals new { revision.QuotationId, revision.Revision }
                    select new
                    {
                        quote.Id, quote.LeadId, LeadName = lead.Name, quote.Status,
                        quote.CurrentRevision, quote.RowVersion, quote.UpdatedAtUtc,
                        revision.Title, revision.Amount, revision.Currency, revision.ValidUntil
                    };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.LeadName.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct = default)
    {
        var quotation = await (from quote in db.SalesQuotations.AsNoTracking()
            join lead in db.Leads.AsNoTracking() on quote.LeadId equals lead.Id
            where quote.Id == id
            select new
            {
                quote.Id, quote.LeadId, LeadName = lead.Name, quote.Status,
                quote.CurrentRevision, quote.RowVersion, quote.CreatedAtUtc, quote.UpdatedAtUtc
            }).FirstOrDefaultAsync(ct);
        if (quotation is null) return NotFound();

        var revisions = await db.SalesQuotationRevisions.AsNoTracking()
            .Where(x => x.QuotationId == id).OrderByDescending(x => x.Revision)
            .Select(x => new
            {
                x.Id, x.Revision, x.Title, x.Scope, x.Amount, x.Currency,
                x.ValidUntil, x.PaymentTerms, x.CreatedById, x.CreatedAtUtc
            }).ToListAsync(ct);
        return Ok(new { quotation, revisions });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateQuotationRequest request, CancellationToken ct)
    {
        if (!Valid(request.Details)) return BadRequest(new { message = "Enter valid quotation details and an amount with at most three decimals." });
        if (!await db.Leads.AnyAsync(x => x.Id == request.LeadId, ct)) return NotFound();
        if (await db.SalesQuotations.AnyAsync(x => x.LeadId == request.LeadId, ct))
            return Conflict(new { message = "This lead already has a quotation. Create a new revision instead." });

        var userId = LeadAccess.UserId(User);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var quote = new SalesQuotation { LeadId = request.LeadId, CreatedById = userId };
        db.SalesQuotations.Add(quote);
        try
        {
            await db.SaveChangesAsync(ct);
            db.SalesQuotationRevisions.Add(Revision(quote.Id, 1, request.Details, userId));
            Audit("Create", quote.Id, "Revision: 1; LeadId: " + quote.LeadId);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return CreatedAtAction(nameof(Get), new { id = quote.Id }, new { quote.Id });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "This lead already has a quotation. Reload the list." });
        }
    }

    [HttpPost("{id:long}/revisions")]
    public async Task<IActionResult> Revise(long id, ReviseQuotationRequest request, CancellationToken ct)
    {
        if (!Valid(request.Details)) return BadRequest(new { message = "Invalid quotation details." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var quote = await db.SalesQuotations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (quote is null) return NotFound();
        if (quote.Status is "Accepted" or "Rejected")
            return Conflict(new { message = "Accepted or rejected quotations cannot be revised." });
        if (!ConcurrencyTokens.Apply(db, quote, request.RowVersion))
            return BadRequest(new { message = "Reload this quotation before editing." });

        quote.CurrentRevision++;
        quote.Status = "Draft";
        quote.UpdatedAtUtc = DateTime.UtcNow;
        db.SalesQuotationRevisions.Add(Revision(id, quote.CurrentRevision, request.Details, LeadAccess.UserId(User)));
        Audit("Revise", id, "Revision: " + quote.CurrentRevision);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { quote.Id, quote.CurrentRevision });
    }

    [HttpPost("{id:long}/status")]
    public async Task<IActionResult> ChangeStatus(long id, ChangeQuotationStatusRequest request, CancellationToken ct)
    {
        if (request.Status is not ("Sent" or "Accepted" or "Rejected"))
            return BadRequest(new { message = "Invalid quotation status." });
        var quote = await db.SalesQuotations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (quote is null) return NotFound();
        if (!(quote.Status == "Draft" && request.Status == "Sent" ||
              quote.Status == "Sent" && request.Status is "Accepted" or "Rejected"))
            return Conflict(new { message = "Invalid quotation transition." });
        if (!ConcurrencyTokens.Apply(db, quote, request.RowVersion))
            return BadRequest(new { message = "Reload this quotation before changing status." });

        var previous = quote.Status;
        quote.Status = request.Status;
        quote.UpdatedAtUtc = DateTime.UtcNow;
        Audit("Status", id, previous + " -> " + request.Status + "; Revision: " + quote.CurrentRevision);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
