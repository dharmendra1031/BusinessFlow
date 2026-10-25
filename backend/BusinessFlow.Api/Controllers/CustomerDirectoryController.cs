using BusinessFlow.Api.Data;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

// Read-only customer directory. Full contact, tax, billing and audit details remain admin-only.
[ApiController]
[Authorize]
[Route("api/customers")]
public sealed class CustomerDirectoryController(
    AppDbContext db, RolePermissionService permissions, RecordAccessService access) : ControllerBase
{
    private const string Action = "customers.view";

    [HttpGet]
    [RequirePermission(Action)]
    public async Task<IActionResult> List(
        string? search = null, int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        if (!await permissions.AllowedAsync(User, Action)) return Forbid();
        if (page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Length > 200)
            return BadRequest(new { message = "Invalid customer filters or pagination." });

        var visibleLeads = (await access.LeadsAsync(User, Action, ct)).AsNoTracking()
            .Where(l => l.CustomerId.HasValue);
        var customers = db.Customers.AsNoTracking()
            .Where(c => visibleLeads.Any(l => l.CustomerId == c.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            customers = customers.Where(c => c.Name.Contains(term));
        }

        var total = await customers.CountAsync(ct);
        var items = await customers.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.Name,
                LeadCount = visibleLeads.Count(l => l.CustomerId == c.Id)
            }).ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:long}")]
    [RequirePermission(Action)]
    public async Task<IActionResult> Get(long id, CancellationToken ct = default)
    {
        if (!await permissions.AllowedAsync(User, Action)) return Forbid();
        var visibleLeads = (await access.LeadsAsync(User, Action, ct)).AsNoTracking()
            .Where(l => l.CustomerId == id);
        if (!await visibleLeads.AnyAsync(ct)) return NotFound();

        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new { c.Id, c.Name }).SingleOrDefaultAsync(ct);
        if (customer is null) return NotFound();
        var leadCount = await visibleLeads.CountAsync(ct);
        var leads = await visibleLeads.OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id)
            .Take(100).Select(l => new { l.Id, l.Name, Status = l.Status.Name })
            .ToListAsync(ct);
        return Ok(new { customer, leadCount, leads });
    }
}
