using BusinessFlow.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/masters")]
public sealed class MastersController(AppDbContext db) : ControllerBase
{
    [HttpGet("lead-sources")]
    public async Task<IActionResult> Sources() => Ok(await db.LeadSources.AsNoTracking()
        .Where(s => s.IsActive).OrderBy(s => s.Id).ToListAsync());

    [HttpGet("lead-statuses")]
    public async Task<IActionResult> Statuses() => Ok(await db.LeadStatuses.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync());
}
