using System.Data;
using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

// Initial Customer Master is limited to administrators until customer data scopes are approved.
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/customers")]
public sealed class CustomersController(AppDbContext db) : ControllerBase
{
    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? PhoneKey(string? phone) =>
        phone is null ? null : new string(phone.Where(char.IsAsciiDigit).ToArray());

    private async Task<IActionResult?> CheckDuplicates(
        SaveCustomerRequest request, long? excluding, CancellationToken ct)
    {
        var email = Optional(request.Email)?.ToLowerInvariant();
        var phone = PhoneKey(Optional(request.Phone));
        var gstin = Optional(request.Gstin)?.ToUpperInvariant();
        if (email is null && phone is null && gstin is null) return null;

        var matches = await db.Customers.AsNoTracking()
            .Where(c => (excluding == null || c.Id != excluding) &&
                (email != null && c.Email != null && c.Email.ToLower() == email ||
                 phone != null && c.NormalizedPhone == phone ||
                 gstin != null && c.Gstin == gstin))
            .OrderBy(c => c.Id)
            .Take(10)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        if (matches.Count == 0) return null;
        return Conflict(new
        {
            message = $"Possible duplicate customer: {matches[0].Name} (#{matches[0].Id}). Review the existing record before saving.",
            matches
        });
    }

    private static void UpdateDetails(Customer customer, SaveCustomerRequest request)
    {
        customer.Name = request.Name.Trim();
        customer.Email = Optional(request.Email);
        customer.Phone = Optional(request.Phone);
        customer.Address = Optional(request.Address);
        customer.LegalName = Optional(request.LegalName);
        customer.Gstin = Optional(request.Gstin)?.ToUpperInvariant();
        customer.BillingAddress = Optional(request.BillingAddress);
        customer.BillingCity = Optional(request.BillingCity);
        customer.BillingState = Optional(request.BillingState);
        customer.BillingPostalCode = Optional(request.BillingPostalCode);
        customer.BillingCountry = Optional(request.BillingCountry);
        customer.IsActive = request.IsActive;
        customer.UpdatedAtUtc = DateTime.UtcNow;
    }

    private void Audit(string action, string table, long id, string? details = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = action,
            TableName = table, RecordId = id, ChangedFields = details
        });

    [HttpGet]
    public async Task<IActionResult> List(string? search = null, bool? active = null,
        int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Length > 200)
            return BadRequest(new { message = "Invalid customer filters or pagination." });

        var customers = db.Customers.AsNoTracking();
        if (active.HasValue) customers = customers.Where(c => c.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            customers = customers.Where(c => c.Name.Contains(term) ||
                c.Email != null && c.Email.Contains(term) ||
                c.Phone != null && c.Phone.Contains(term) ||
                c.Gstin != null && c.Gstin.Contains(term) ||
                c.LegalName != null && c.LegalName.Contains(term));
        }
        var total = await customers.CountAsync(ct);
        var items = await customers.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.Name, c.Email, c.Phone, c.IsActive, c.RowVersion,
                LeadCount = db.Leads.Count(l => l.CustomerId == c.Id),
                ProjectCount = db.DevelopmentProjects.Count(p => p.Lead.CustomerId == c.Id)
            }).ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id, c.Name, c.Email, c.Phone, c.Address, c.IsActive,
                c.LegalName, c.Gstin, c.BillingAddress, c.BillingCity, c.BillingState,
                c.BillingPostalCode, c.BillingCountry,
                c.RowVersion, c.CreatedAtUtc, c.UpdatedAtUtc
            }).FirstOrDefaultAsync(ct);
        if (customer is null) return NotFound();
        var contacts = await db.CustomerContacts.AsNoTracking()
            .Where(c => c.CustomerId == id)
            .OrderByDescending(c => c.IsPrimary).ThenByDescending(c => c.IsActive).ThenBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name, c.Designation, c.Email, c.Phone, c.IsPrimary, c.IsActive
            }).ToListAsync(ct);
        var leadCount = await db.Leads.CountAsync(l => l.CustomerId == id, ct);
        var leads = await db.Leads.AsNoTracking().Where(l => l.CustomerId == id)
            .OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id).Take(100)
            .Select(l => new { l.Id, l.Name, Status = l.Status.Name, l.RowVersion })
            .ToListAsync(ct);
        var projectCount = await db.DevelopmentProjects.CountAsync(p => p.Lead.CustomerId == id, ct);
        var projects = await db.DevelopmentProjects.AsNoTracking().Where(p => p.Lead.CustomerId == id)
            .OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Take(100)
            .Select(p => new { p.Id, p.Title, p.Status, p.LeadId, p.OwnerId })
            .ToListAsync(ct);
        return Ok(new { customer, contacts, leadCount, leads, projectCount, projects });
    }


    [HttpGet("{id:long}/history")]
    public async Task<IActionResult> History(long id, int page = 1, int pageSize = 30,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { message = "Invalid history pagination." });
        if (!await db.Customers.AnyAsync(c => c.Id == id, ct)) return NotFound();

        var contactIds = db.CustomerContacts.Where(c => c.CustomerId == id).Select(c => c.Id);
        var linkMarker = "CustomerId: " + id;
        var history = db.AuditLogs.AsNoTracking().Where(a =>
            a.TableName == "Customers" && a.RecordId == id ||
            a.TableName == "CustomerContacts" && contactIds.Contains(a.RecordId) ||
            a.TableName == "Leads" && (a.Action == "Link" || a.Action == "Unlink") &&
                a.ChangedFields == linkMarker);
        var total = await history.CountAsync(ct);
        var items = await history.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new { a.Id, a.Action, a.TableName, a.RecordId, a.UserId, a.CreatedAtUtc })
            .ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("unlinked-leads")]
    public async Task<IActionResult> UnlinkedLeads(string? search = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Length > 150)
            return BadRequest(new { message = "Enter a lead name, code or phone to search." });
        var term = search.Trim();
        var items = await db.Leads.AsNoTracking().Where(l => l.CustomerId == null &&
                (l.Name.Contains(term) || l.Phone.Contains(term) ||
                 l.CustomerCode != null && l.CustomerCode.Contains(term)))
            .OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id).Take(30)
            .Select(l => new { l.Id, l.Name, l.Phone, Status = l.Status.Name, l.RowVersion })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaveCustomerRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 200)
            return BadRequest(new { message = "Customer name must contain 2 to 200 characters." });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var duplicate = await CheckDuplicates(request, null, ct);
        if (duplicate is not null) return duplicate;
        var customer = new Customer();
        UpdateDetails(customer, request);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        Audit("Create", "Customers", customer.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, new { customer.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, SaveCustomerRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 200)
            return BadRequest(new { message = "Customer name must contain 2 to 200 characters." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();
        if (!ConcurrencyTokens.Apply(db, customer, request.RowVersion))
            return BadRequest(new { message = "A valid row version is required. Reload this customer." });
        var duplicate = await CheckDuplicates(request, id, ct);
        if (duplicate is not null) return duplicate;
        UpdateDetails(customer, request);
        Audit("Update", "Customers", customer.Id, "Customer, billing or active state updated");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:long}/contacts")]
    public Task<IActionResult> AddContact(long id, SaveCustomerContactRequest request, CancellationToken ct) =>
        SaveContact(id, null, request, ct);

    [HttpPut("{id:long}/contacts/{contactId:long}")]
    public Task<IActionResult> UpdateContact(long id, long contactId,
        SaveCustomerContactRequest request, CancellationToken ct) =>
        SaveContact(id, contactId, request, ct);

    private async Task<IActionResult> SaveContact(long id, long? contactId,
        SaveCustomerContactRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 150 || request.IsPrimary && !request.IsActive)
            return BadRequest(new { message = "Enter a valid contact name and primary status." });

        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        if (!await db.Customers.AnyAsync(c => c.Id == id, ct)) return NotFound();

        CustomerContact? contact = null;
        if (contactId.HasValue)
        {
            contact = await db.CustomerContacts.FirstOrDefaultAsync(c =>
                c.Id == contactId && c.CustomerId == id, ct);
            if (contact is null) return NotFound();
        }
        else
        {
            contact = new CustomerContact { CustomerId = id };
            db.CustomerContacts.Add(contact);
        }

        if (request.IsPrimary)
        {
            var previous = await db.CustomerContacts.Where(c => c.CustomerId == id &&
                c.Id != contact.Id && c.IsPrimary).ToListAsync(ct);
            foreach (var entry in previous) entry.IsPrimary = false;
        }

        contact.Name = name;
        contact.Designation = Optional(request.Designation);
        contact.Phone = Optional(request.Phone);
        contact.Email = Optional(request.Email);
        contact.IsActive = request.IsActive;
        contact.IsPrimary = request.IsPrimary;
        await db.SaveChangesAsync(ct);
        Audit(contactId.HasValue ? "Update" : "Create", "CustomerContacts", contact.Id,
            "CustomerId: " + id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { contact.Id });
    }

    [HttpPut("{id:long}/leads/{leadId:long}")]
    public async Task<IActionResult> LinkLead(long id, long leadId,
        LinkCustomerLeadRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Customers.AnyAsync(c => c.Id == id && c.IsActive, ct))
            return NotFound(new { message = "Active customer not found." });
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return NotFound();
        if (lead.CustomerId.HasValue && lead.CustomerId != id)
            return Conflict(new { message = "This lead is already linked to another customer." });
        if (!ConcurrencyTokens.Apply(db, lead, request.RowVersion))
            return BadRequest(new { message = "A valid lead row version is required." });
        if (lead.CustomerId == id) return NoContent();
        lead.CustomerId = id;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        Audit("Link", "Leads", lead.Id, "CustomerId: " + id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:long}/leads/{leadId:long}")]
    public async Task<IActionResult> UnlinkLead(long id, long leadId,
        [FromBody] LinkCustomerLeadRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null || lead.CustomerId != id) return NotFound();
        if (!ConcurrencyTokens.Apply(db, lead, request.RowVersion))
            return BadRequest(new { message = "A valid lead row version is required." });
        lead.CustomerId = null;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        Audit("Unlink", "Leads", lead.Id, "CustomerId: " + id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }
}
