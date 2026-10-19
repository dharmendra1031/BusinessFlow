using BusinessFlow.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/audit")]
public sealed class AuditController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        string? table = null, string? action = null, long? userId = null,
        DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 25)
    {
        if (page < 1 || pageSize is < 1 or > 100 ||
            (from.HasValue && to.HasValue && from > to) ||
            table?.Length > 60 || action?.Length > 50 || userId is <= 0)
            return BadRequest(new { message = "Invalid audit filters or pagination." });

        var entries = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(table))
            entries = entries.Where(x => x.TableName == table.Trim());
        if (!string.IsNullOrWhiteSpace(action))
            entries = entries.Where(x => x.Action == action.Trim());
        if (userId.HasValue) entries = entries.Where(x => x.UserId == userId);
        if (from.HasValue)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entries = entries.Where(x => x.CreatedAtUtc >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entries = entries.Where(x => x.CreatedAtUtc < end);
        }

        var total = await entries.CountAsync();
        var items = await entries
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.CreatedAtUtc, x.TableName, x.RecordId, x.Action, x.ChangedFields,
                x.UserId,
                UserName = db.Users.Where(u => u.Id == x.UserId)
                    .Select(u => u.FullName).FirstOrDefault()
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }
}
