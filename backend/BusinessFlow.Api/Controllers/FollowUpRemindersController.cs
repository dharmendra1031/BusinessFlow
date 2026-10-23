using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/followups/reminders")]
public sealed class FollowUpRemindersController(AppDbContext db, RecordAccessService records) : ControllerBase
{
    [HttpGet]
    [RequirePermission("followups.view")]
    public async Task<IActionResult> List(string view = "due", int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        if (view is not ("due" or "upcoming" or "all") || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { message = "Invalid reminder filter or pagination." });

        var now = DateTime.UtcNow;
        var employeeId = LeadAccess.UserId(User);
        var visible = (await records.LeadsAsync(User, "followups.view", ct)).Select(x => x.Id);
        var query =
            from followUp in db.LeadFollowUps.AsNoTracking()
            join reminder in db.FollowUpReminderStates.AsNoTracking().Where(x => x.UserId == employeeId)
                on followUp.Id equals reminder.FollowUpId into reminders
            from state in reminders.DefaultIfEmpty()
            where followUp.AssignedToId == employeeId && followUp.CompletedAtUtc == null &&
                visible.Contains(followUp.LeadId) &&
                (state == null || (state.AcknowledgedAtUtc == null &&
                    (state.SnoozedUntilUtc == null || state.SnoozedUntilUtc <= now)))
            select new { followUp, state };

        if (view == "due") query = query.Where(x => x.followUp.DueAtUtc <= now);
        else if (view == "upcoming")
            query = query.Where(x => x.followUp.DueAtUtc > now &&
                x.followUp.DueAtUtc <= now.AddHours(24));
        else query = query.Where(x => x.followUp.DueAtUtc <= now.AddDays(7));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.followUp.DueAtUtc).ThenBy(x => x.followUp.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.followUp.Id, x.followUp.LeadId,
                LeadName = x.followUp.Lead.Name, x.followUp.Notes,
                x.followUp.DueAtUtc, x.followUp.AssignedToId,
                IsOverdue = x.followUp.DueAtUtc < now
            }).ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }

    public sealed record SnoozeRequest(int Minutes);

    [HttpPost("{id:long}/snooze")]
    [RequirePermission("followups.view")]
    public async Task<IActionResult> Snooze(long id, SnoozeRequest request, CancellationToken ct)
    {
        if (request.Minutes is not (15 or 60 or 1440))
            return BadRequest(new { message = "Snooze must be 15 minutes, 1 hour or 1 day." });
        return await Change(id, request.Minutes, ct);
    }

    [HttpPost("{id:long}/acknowledge")]
    [RequirePermission("followups.view")]
    public Task<IActionResult> Acknowledge(long id, CancellationToken ct) => Change(id, null, ct);

    private async Task<IActionResult> Change(long id, int? snoozeMinutes, CancellationToken ct)
    {
        var employeeId = LeadAccess.UserId(User);
        var accessible = (await records.LeadsAsync(User, "followups.view", ct)).Select(x => x.Id);
        var followUp = await db.LeadFollowUps.AsNoTracking()
            .Where(x => x.Id == id && x.AssignedToId == employeeId &&
                x.CompletedAtUtc == null && accessible.Contains(x.LeadId))
            .Select(x => new { x.Id, x.DueAtUtc })
            .SingleOrDefaultAsync(ct);
        if (followUp is null) return NotFound();
        var now = DateTime.UtcNow;
        if (followUp.DueAtUtc > now && !snoozeMinutes.HasValue)
            return BadRequest(new { message = "Acknowledge a reminder only after it is due." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await db.FollowUpReminderStates
            .SingleOrDefaultAsync(x => x.FollowUpId == id && x.UserId == employeeId, ct);
        if (state is null)
        {
            state = new FollowUpReminderState { FollowUpId = id, UserId = employeeId };
            db.FollowUpReminderStates.Add(state);
        }
        state.AcknowledgedAtUtc = snoozeMinutes is null ? now : null;
        state.SnoozedUntilUtc = snoozeMinutes.HasValue ? now.AddMinutes(snoozeMinutes.Value) : null;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = employeeId, Action = snoozeMinutes is null ? "Acknowledge" : "Snooze",
            TableName = "FollowUpReminders", RecordId = id,
            ChangedFields = snoozeMinutes.HasValue ? $"Minutes={snoozeMinutes}" : null
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConflict(ex))
        {
            return Conflict(new { message = "Reminder changed. Refresh and try again." });
        }
        return NoContent();
    }

    private static bool IsUniqueConflict(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 2601 or 2627 }) return true;
        return false;
    }
}
