using LeadManagement.Api.Contracts;
using LeadManagement.Api.Data;
using LeadManagement.Api.Models;
using LeadManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
public sealed class AdminUsersController(AppDbContext db, UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpdateEmployeeRequest request)
    {
        var employee = await users.FindByIdAsync(id.ToString());
        if (employee is null) return NotFound();
        if (request.ManagerId == id) return BadRequest(new { message = "Invalid reporting manager." });
        if (request.ManagerId.HasValue)
        {
            var manager = await users.FindByIdAsync(request.ManagerId.Value.ToString());
            if (manager is null || !manager.IsActive || !await users.IsInRoleAsync(manager, "Manager"))
                return BadRequest(new { message = "Manager not found." });
        }
        if (request.Role is not null && request.Role is not ("Employee" or "Manager"))
            return BadRequest(new { message = "Invalid role." });
        if (await users.IsInRoleAsync(employee, "Admin") && request.Role is not null)
            return BadRequest(new { message = "Cannot modify admin role." });

        await using var transaction = await db.Database.BeginTransactionAsync();
        employee.FullName = request.FullName.Trim();
        employee.PhoneNumber = request.PhoneNumber?.Trim();
        employee.ManagerId = request.ManagerId;
        var result = await users.UpdateAsync(employee);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        if (request.Role is not null && !await users.IsInRoleAsync(employee, request.Role))
        {
            var current = await users.GetRolesAsync(employee);
            result = await users.RemoveFromRolesAsync(employee, current);
            if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            result = await users.AddToRoleAsync(employee, request.Role);
            if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            await users.UpdateSecurityStampAsync(employee);
        }
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = "Update",
            TableName = "Users", RecordId = id, ChangedFields = "Profile, role, manager"
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpPatch("{id:long}/status")]
    public async Task<IActionResult> Status(long id, [FromBody] bool isActive)
    {
        if (id == LeadAccess.UserId(User))
            return BadRequest(new { message = "You cannot deactivate your own account." });
        var employee = await users.FindByIdAsync(id.ToString());
        if (employee is null) return NotFound();

        employee.IsActive = isActive;
        var result = await users.UpdateAsync(employee);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        await users.UpdateSecurityStampAsync(employee);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = isActive ? "Activate" : "Deactivate",
            TableName = "Users", RecordId = id
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:long}/summary")]
    public async Task<IActionResult> Summary(long id)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound();
        var leads = db.Leads.AsNoTracking().Where(l => l.AssignedToId == id);
        return Ok(new
        {
            customers = await leads.CountAsync(),
            projectAmount = await leads.SumAsync(l => l.ProjectAmount ?? 0m)
        });
    }
}
