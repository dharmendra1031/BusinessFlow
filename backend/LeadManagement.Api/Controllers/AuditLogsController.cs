using LeadManagement.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/audit-logs")]
public sealed class AuditLogsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int page = 1, int pageSize = 25,
        string? table = null, string? action = null, long? userId = null,
        DateOnly? from = null, DateOnly? to = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (from.HasValue && to.HasValue && from > to) ||
            table?.Length > 60 || action?.Length > 50 || userId is <= 0)
            return BadRequest(new { message = "Invalid audit log filters." });

        var logs = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(table)) logs = logs.Where(l => l.TableName == table.Trim());
        if (!string.IsNullOrWhiteSpace(action)) logs = logs.Where(l => l.Action == action.Trim());
        if (userId.HasValue) logs = logs.Where(l => l.UserId == userId.Value);
        if (from.HasValue) logs = logs.Where(l => l.CreatedAtUtc >= from.Value.ToDateTime(TimeOnly.MinValue));
        if (to.HasValue) logs = logs.Where(l => l.CreatedAtUtc < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var total = await logs.CountAsync();
        var items = await (from log in logs
            join member in db.Users.AsNoTracking() on log.UserId equals member.Id into members
            from member in members.DefaultIfEmpty()
            orderby log.CreatedAtUtc descending, log.Id descending
            select new
            {
                log.Id, log.CreatedAtUtc, log.UserId,
                UserName = member == null ? "Deleted user" : member.FullName,
                log.Action, log.TableName, log.RecordId, log.ChangedFields
            }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }
}
