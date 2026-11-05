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
    private const decimal MaxAmount = 999999999999999.999m;

    private sealed record LineAmount(QuotationLineRequest Source, decimal Base, decimal Discount, decimal Tax, decimal Total);
    private sealed record QuotationAmount(decimal Subtotal, decimal Discount, decimal Tax, decimal Total, List<LineAmount> Lines);

    private static decimal Round(decimal value) => decimal.Round(value, 3, MidpointRounding.AwayFromZero);

    private static QuotationAmount? Calculate(QuotationDetails? details)
    {
        if (details is null || string.IsNullOrWhiteSpace(details.Title) ||
            details.Title.Trim().Length is < 2 or > 150 ||
            (details.Scope?.Trim().Length ?? 0) > 2000 ||
            (details.PaymentTerms?.Trim().Length ?? 0) > 1000 ||
            details.Currency is null || details.Currency.Length != 3 ||
            !details.Currency.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')) return null;

        if (details.Items is null)
        {
            if (details.Amount <= 0 || details.Amount > MaxAmount || Round(details.Amount) != details.Amount)
                return null;
            return new(details.Amount, 0, 0, details.Amount, []);
        }

        if (details.Items.Count is < 1 or > 100) return null;
        var lines = new List<LineAmount>(details.Items.Count);
        decimal subtotal = 0, discount = 0, tax = 0, total = 0;
        try
        {
            foreach (var line in details.Items)
            {
                if (line is null || string.IsNullOrWhiteSpace(line.Description) ||
                    line.Description.Trim().Length is < 2 or > 250 ||
                    line.Quantity <= 0 || line.Quantity > 999999999.999m ||
                    line.UnitPrice < 0 || line.UnitPrice > MaxAmount ||
                    line.DiscountRate is < 0 or > 100 || line.TaxRate is < 0 or > 100 ||
                    Round(line.Quantity) != line.Quantity || Round(line.UnitPrice) != line.UnitPrice ||
                    decimal.Round(line.DiscountRate, 2) != line.DiscountRate ||
                    decimal.Round(line.TaxRate, 2) != line.TaxRate) return null;

                var basis = Round(line.Quantity * line.UnitPrice);
                var disc = Round(basis * line.DiscountRate / 100m);
                var levy = Round((basis - disc) * line.TaxRate / 100m);
                var gross = basis - disc + levy;
                subtotal += basis; discount += disc; tax += levy; total += gross;
                if (subtotal > MaxAmount || total > MaxAmount) return null;
                lines.Add(new(line, basis, disc, levy, gross));
            }
        }
        catch (OverflowException) { return null; }
        return total <= 0 ? null : new(subtotal, discount, tax, total, lines);
    }

    private static SalesQuotationRevision Revision(long quotationId, int number,
        QuotationDetails details, QuotationAmount values, long userId) => new()
    {
        QuotationId = quotationId,
        Revision = number,
        Title = details.Title.Trim(),
        Scope = string.IsNullOrWhiteSpace(details.Scope) ? null : details.Scope.Trim(),
        Amount = values.Total,
        Subtotal = values.Subtotal,
        DiscountAmount = values.Discount,
        TaxAmount = values.Tax,
        Currency = details.Currency.ToUpperInvariant(),
        ValidUntil = details.ValidUntil,
        PaymentTerms = string.IsNullOrWhiteSpace(details.PaymentTerms) ? null : details.PaymentTerms.Trim(),
        CreatedById = userId,
        Items = values.Lines.Select((x, i) => new SalesQuotationLineItem
        {
            Position = i + 1, Description = x.Source.Description.Trim(),
            Quantity = x.Source.Quantity, UnitPrice = x.Source.UnitPrice,
            DiscountRate = x.Source.DiscountRate, TaxRate = x.Source.TaxRate,
            BaseAmount = x.Base, DiscountAmount = x.Discount,
            TaxAmount = x.Tax, TotalAmount = x.Total
        }).ToList()
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
                        revision.Title, revision.Amount, revision.Subtotal, revision.DiscountAmount, revision.TaxAmount, revision.Currency, revision.ValidUntil
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
                quote.CurrentRevision, quote.RowVersion, quote.CreatedAtUtc, quote.UpdatedAtUtc, quote.ApprovalRequestedAtUtc, quote.ApprovedById,
                quote.ApprovedAtUtc, quote.ApprovalComment, quote.SentAtUtc, quote.ClientResponseBy,
                quote.ClientResponseReference, quote.ClientRespondedAtUtc
            }).FirstOrDefaultAsync(ct);
        if (quotation is null) return NotFound();

        var revisions = await db.SalesQuotationRevisions.AsNoTracking()
            .Where(x => x.QuotationId == id).OrderByDescending(x => x.Revision)
            .Select(x => new
            {
                x.Id, x.Revision, x.Title, x.Scope, x.Amount, x.Subtotal, x.DiscountAmount,
                x.TaxAmount, x.Currency, x.ValidUntil, x.PaymentTerms, x.CreatedById, x.CreatedAtUtc,
                Items = x.Items.OrderBy(i => i.Position).Select(i => new
                {
                    i.Id, i.Position, i.Description, i.Quantity, i.UnitPrice,
                    i.DiscountRate, i.TaxRate, i.BaseAmount, i.DiscountAmount,
                    i.TaxAmount, i.TotalAmount
                }).ToList()
            }).ToListAsync(ct);
        var contract = await db.SalesContracts.AsNoTracking().Where(x => x.QuotationId == id)
            .Select(x => new { x.Id, x.QuotationId, x.QuotationRevision, x.Status, x.Terms,
                x.CreatedAtUtc, x.SignedBy, x.SignatureReference, x.SignedOn, x.RowVersion })
            .FirstOrDefaultAsync(ct);
        return Ok(new { quotation, revisions, contract });
    }

    [HttpGet("{id:long}/pdf")]
    public async Task<IActionResult> Pdf(long id, int? revision = null, CancellationToken ct = default)
    {
        var header = await (from quotation in db.SalesQuotations.AsNoTracking()
            join lead in db.Leads.AsNoTracking() on quotation.LeadId equals lead.Id
            join customer in db.Customers.AsNoTracking() on lead.CustomerId equals customer.Id into customers
            from customer in customers.DefaultIfEmpty()
            where quotation.Id == id
            select new
            {
                quotation.Id, quotation.Status, quotation.CurrentRevision,
                ClientName = customer == null ? (lead.CompanyName ?? lead.Name) :
                    (customer.LegalName ?? customer.Name)
            }).FirstOrDefaultAsync(ct);
        if (header is null) return NotFound();
        var number = revision ?? header.CurrentRevision;
        if (number < 1 || number > header.CurrentRevision) return NotFound();
        var current = await db.SalesQuotationRevisions.AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.QuotationId == id && r.Revision == number, ct);
        if (current is null) return NotFound();

        var renderer = ReportPdfRenderer.Path(HttpContext.RequestServices.GetRequiredService<IConfiguration>());
        if (!System.IO.File.Exists(renderer))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "PDF renderer is unavailable." });

        Dictionary<string, object?> Row(string description, decimal? quantity = null,
            decimal? price = null, decimal? discount = null, decimal? tax = null, decimal? total = null) => new()
        {
            ["quoteRef"] = $"Q-{header.Id:D6}",
            ["revision"] = (long)number,
            ["clientName"] = header.ClientName,
            ["status"] = number == header.CurrentRevision ? header.Status : "Superseded",
            ["currency"] = current.Currency,
            ["validUntil"] = current.ValidUntil?.ToString("yyyy-MM-dd") ?? "Not specified",
            ["description"] = description,
            ["quantity"] = quantity, ["unitPrice"] = price, ["discount"] = discount,
            ["tax"] = tax, ["lineTotal"] = total
        };

        var rows = current.Items.OrderBy(x => x.Position)
            .Select(x => Row(x.Description, x.Quantity, x.UnitPrice,
                x.DiscountAmount, x.TaxAmount, x.TotalAmount)).ToList();
        if (rows.Count == 0)
            rows.Add(Row(current.Title, 1m, current.Amount, 0, 0, current.Amount));
        rows.Add(Row("SUBTOTAL", total: current.Subtotal));
        if (current.DiscountAmount > 0) rows.Add(Row("DISCOUNT", total: -current.DiscountAmount));
        if (current.TaxAmount > 0) rows.Add(Row("TAX", total: current.TaxAmount));
        rows.Add(Row("GRAND TOTAL", total: current.Amount));
        if (!string.IsNullOrWhiteSpace(current.Scope)) rows.Add(Row("SCOPE: " + current.Scope));
        if (!string.IsNullOrWhiteSpace(current.PaymentTerms)) rows.Add(Row("PAYMENT TERMS: " + current.PaymentTerms));

        try
        {
            var pdf = await ReportPdfRenderer.RenderAsync(renderer, "quotation",
                System.Text.Json.JsonSerializer.Serialize(rows), ct);
            return File(pdf, "application/pdf", $"Quotation-{id}-R{number}.pdf");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<QuotationsController>>()
                .LogError(error, "Quotation PDF generation failed for {QuotationId}", id);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Quotation PDF generation failed." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateQuotationRequest request, CancellationToken ct)
    {
        var amount = Calculate(request.Details);
        if (amount is null) return BadRequest(new { message = "Invalid quotation line items or total." });
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
            db.SalesQuotationRevisions.Add(Revision(quote.Id, 1, request.Details, amount, userId));
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
        var amount = Calculate(request.Details);
        if (amount is null) return BadRequest(new { message = "Invalid quotation line items or total." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var quote = await db.SalesQuotations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (quote is null) return NotFound();
        if (quote.Status is not ("Draft" or "Approved" or "Sent"))
            return Conflict(new { message = "Only draft, approved or sent quotations can be revised." });
        if (!ConcurrencyTokens.Apply(db, quote, request.RowVersion))
            return BadRequest(new { message = "Reload this quotation before editing." });

        quote.CurrentRevision++;
        quote.Status = "Draft";
        quote.ApprovalRequestedAtUtc = null;
        quote.ApprovedById = null;
        quote.ApprovedAtUtc = null;
        quote.ApprovalComment = null;
        quote.SentAtUtc = null;
        quote.ClientResponseBy = null;
        quote.ClientResponseReference = null;
        quote.ClientRespondedAtUtc = null;
        quote.UpdatedAtUtc = DateTime.UtcNow;
        db.SalesQuotationRevisions.Add(Revision(id, quote.CurrentRevision, request.Details, amount, LeadAccess.UserId(User)));
        Audit("Revise", id, "Revision: " + quote.CurrentRevision);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { quote.Id, quote.CurrentRevision });
    }

    [HttpPost("{id:long}/submit")]
    public async Task<IActionResult> SubmitForApproval(long id, SubmitQuotationRequest request, CancellationToken ct)
    {
        var quote = await db.SalesQuotations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (quote is null) return NotFound();
        if (quote.Status != "Draft")
            return Conflict(new { message = "Only draft quotations can be submitted for approval." });
        if (!ConcurrencyTokens.Apply(db, quote, request.RowVersion))
            return BadRequest(new { message = "Reload the quotation before submitting." });
        quote.Status = "PendingApproval";
        quote.ApprovalRequestedAtUtc = DateTime.UtcNow;
        quote.UpdatedAtUtc = DateTime.UtcNow;
        Audit("Submit", id, "Revision: " + quote.CurrentRevision);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:long}/review")]
    public async Task<IActionResult> Review(long id, ReviewQuotationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Comment) || request.Comment.Trim().Length is < 10 or > 500)
            return BadRequest(new { message = "Add a review comment of 10 to 500 characters." });
        var quote = await db.SalesQuotations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (quote is null) return NotFound();
        if (quote.Status != "PendingApproval")
            return Conflict(new { message = "Only pending quotations can be reviewed." });
        if (!ConcurrencyTokens.Apply(db, quote, request.RowVersion))
            return BadRequest(new { message = "Reload the quotation before review." });

        quote.Status = request.Approve ? "Approved" : "Draft";
        quote.ApprovalComment = request.Comment.Trim();
        quote.ApprovedById = request.Approve ? LeadAccess.UserId(User) : null;
        quote.ApprovedAtUtc = request.Approve ? DateTime.UtcNow : null;
        quote.UpdatedAtUtc = DateTime.UtcNow;
        Audit(request.Approve ? "Approve" : "Return", id,
            "Revision: " + quote.CurrentRevision + "; " + request.Comment.Trim());
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:long}/status")]
    public async Task<IActionResult> ChangeStatus(long id, ChangeQuotationStatusRequest request, CancellationToken ct)
    {
        if (request.Status is not ("Sent" or "Accepted" or "Rejected"))
            return BadRequest(new { message = "Invalid quotation status." });
        var quote = await db.SalesQuotations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (quote is null) return NotFound();
        if (!(quote.Status == "Approved" && request.Status == "Sent" ||
              quote.Status == "Sent" && request.Status is "Accepted" or "Rejected"))
            return Conflict(new { message = "Approval is required before sending or recording a client decision." });
        if (!ConcurrencyTokens.Apply(db, quote, request.RowVersion))
            return BadRequest(new { message = "Reload this quotation before changing status." });
        if (request.Status != "Sent" &&
            (string.IsNullOrWhiteSpace(request.ClientResponseBy) ||
             request.ClientResponseBy.Trim().Length is < 2 or > 150 ||
             string.IsNullOrWhiteSpace(request.ClientResponseReference) ||
             request.ClientResponseReference.Trim().Length is < 5 or > 500))
            return BadRequest(new { message = "Enter the client contact and a verifiable response reference." });

        var previous = quote.Status;
        quote.Status = request.Status;
        quote.UpdatedAtUtc = DateTime.UtcNow;
        if (request.Status == "Sent") quote.SentAtUtc = DateTime.UtcNow;
        else
        {
            quote.ClientResponseBy = request.ClientResponseBy!.Trim();
            quote.ClientResponseReference = request.ClientResponseReference!.Trim();
            quote.ClientRespondedAtUtc = DateTime.UtcNow;
        }
        Audit("Status", id, previous + " -> " + request.Status + "; Revision: " + quote.CurrentRevision +
            (request.Status == "Sent" ? "" : "; Response: " + request.ClientResponseReference!.Trim()));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:long}/contract")]
    public async Task<IActionResult> CreateContract(long id, CreateSalesContractRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Terms) || request.Terms.Trim().Length is < 10 or > 2000)
            return BadRequest(new { message = "Contract terms must contain 10 to 2000 characters." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var quote = await db.SalesQuotations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (quote is null) return NotFound();
        if (quote.Status != "Accepted")
            return Conflict(new { message = "Only accepted quotations can become contract drafts." });
        if (!ConcurrencyTokens.Apply(db, quote, request.RowVersion))
            return BadRequest(new { message = "Reload this quotation before creating a contract." });
        if (await db.SalesContracts.AnyAsync(x => x.QuotationId == id, ct))
            return Conflict(new { message = "A contract already exists for this quotation." });

        var contract = new SalesContract
        {
            QuotationId = id, QuotationRevision = quote.CurrentRevision,
            Terms = request.Terms.Trim(), CreatedById = LeadAccess.UserId(User)
        };
        db.SalesContracts.Add(contract);
        try
        {
            await db.SaveChangesAsync(ct);
            Audit("Contract", id, "ContractId: " + contract.Id + "; Revision: " + contract.QuotationRevision);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(new { contract.Id });
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "A contract already exists for this quotation." });
        }
    }

    [HttpPost("{id:long}/contract/sign")]
    public async Task<IActionResult> SignContract(long id, SignSalesContractRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SignedBy) || request.SignedBy.Trim().Length is < 2 or > 150 ||
            string.IsNullOrWhiteSpace(request.SignatureReference) ||
            request.SignatureReference.Trim().Length is < 5 or > 500 ||
            request.SignedOn == default ||
            request.SignedOn > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return BadRequest(new { message = "Provide the signer, evidence reference and valid signed date." });

        var contract = await db.SalesContracts.FirstOrDefaultAsync(x => x.QuotationId == id, ct);
        if (contract is null) return NotFound();
        if (contract.Status != "Draft")
            return Conflict(new { message = "This contract is already marked signed." });
        if (!ConcurrencyTokens.Apply(db, contract, request.RowVersion))
            return BadRequest(new { message = "Reload the contract before recording its signature." });

        contract.Status = "Signed";
        contract.SignedBy = request.SignedBy.Trim();
        contract.SignatureReference = request.SignatureReference.Trim();
        contract.SignedOn = request.SignedOn;
        Audit("ContractSigned", id, "ContractId: " + contract.Id + "; Evidence: " + contract.SignatureReference);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
