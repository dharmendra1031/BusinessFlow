using System.Data;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

public sealed record ClientAccessRequest(long CustomerId, long ContactId);
public sealed record ClientAccessStatusRequest(bool IsActive);

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/client-access")]
public sealed class ClientAccessController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000) return BadRequest();
        var accounts = await (from account in db.ClientPortalAccounts.AsNoTracking()
                              join customer in db.Customers.AsNoTracking() on account.CustomerId equals customer.Id
                              join contact in db.CustomerContacts.AsNoTracking() on account.ContactId equals contact.Id
                              orderby account.Id descending
                              select new
                              {
                                  account.Id, account.CustomerId, Customer = customer.Name,
                                  account.ContactId, Contact = contact.Name, account.Email,
                                  account.IsActive, account.CreatedAtUtc, account.RevokedAtUtc
                              })
            .Skip((page - 1) * 30).Take(30).ToListAsync(ct);
        return Ok(new { page, items = accounts });
    }

    [HttpPost]
    public async Task<IActionResult> Create(ClientAccessRequest request, CancellationToken ct)
    {
        if (request.CustomerId <= 0 || request.ContactId <= 0) return BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var customer = await db.Customers.AsNoTracking()
            .Where(x => x.Id == request.CustomerId && x.IsActive && x.MergedIntoCustomerId == null)
            .Select(x => new { x.Id }).SingleOrDefaultAsync(ct);
        var contact = await db.CustomerContacts.AsNoTracking()
            .Where(x => x.Id == request.ContactId && x.CustomerId == request.CustomerId && x.IsActive)
            .Select(x => new { x.Id, x.Email }).SingleOrDefaultAsync(ct);
        if (customer is null || contact is null ||
            string.IsNullOrWhiteSpace(contact.Email) ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(contact.Email))
            return BadRequest(new { message = "Select an active customer and verified company contact with an email address." });

        var email = contact.Email.Trim().ToUpperInvariant();
        if (await db.ClientPortalAccounts.AnyAsync(x => x.ContactId == request.ContactId ||
            x.IsActive && x.Email == email, ct))
            return Conflict(new { message = "This contact or email already has portal access. Review existing accounts." });

        var account = new ClientPortalAccount
        {
            CustomerId = request.CustomerId,
            ContactId = request.ContactId,
            Email = email,
            CreatedById = LeadAccess.UserId(User)
        };
        db.ClientPortalAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User),
            TableName = "ClientPortalAccounts", RecordId = account.Id,
            Action = "Create", ChangedFields = "Admin-approved customer contact"
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Created($"/api/admin/client-access/{account.Id}", new { account.Id });
    }

    [HttpPatch("{id:long}/status")]
    public async Task<IActionResult> SetStatus(long id, ClientAccessStatusRequest request, CancellationToken ct)
    {
        if (id <= 0) return BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await db.ClientPortalAccounts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (account is null) return NotFound();

        if (request.IsActive)
        {
            var allowed = await (from contact in db.CustomerContacts.AsNoTracking()
                                 join customer in db.Customers.AsNoTracking() on contact.CustomerId equals customer.Id
                                 where contact.Id == account.ContactId &&
                                       customer.Id == account.CustomerId &&
                                       contact.IsActive && customer.IsActive &&
                                       customer.MergedIntoCustomerId == null &&
                                       contact.Email != null
                                 select contact.Email).SingleOrDefaultAsync(ct);
            if (allowed is null || !string.Equals(
                allowed.Trim().ToUpperInvariant(), account.Email, StringComparison.Ordinal))
                return Conflict(new { message = "Customer or contact details have changed. Reapproval required." });
            if (await db.ClientPortalAccounts.AnyAsync(x => x.Id != id &&
                x.IsActive && x.Email == account.Email, ct))
                return Conflict(new { message = "Email is used by another active portal account." });
        }

        if (account.IsActive == request.IsActive) return NoContent();
        account.IsActive = request.IsActive;
        account.RevokedAtUtc = request.IsActive ? null : DateTime.UtcNow;
        account.SessionVersion++;
        var now = DateTime.UtcNow;
        var pending = await db.ClientLoginChallenges
            .Where(x => x.AccountId == id && x.UsedAtUtc == null).ToListAsync(ct);
        foreach (var challenge in pending) challenge.UsedAtUtc = now;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User),
            TableName = "ClientPortalAccounts", RecordId = account.Id,
            Action = "Update", ChangedFields = request.IsActive ? "Portal access enabled" : "Portal access revoked"
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }
}
