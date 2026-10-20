using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
public sealed class AdminUsersController(AppDbContext db, UserManager<ApplicationUser> users, OrganizationService organization) : ControllerBase
{
    [HttpPut("{id:long}")]
    [RequirePermission("employees.edit")]
    public async Task<IActionResult> Update(long id, UpdateEmployeeRequest request)
    {
        var employee = await users.FindByIdAsync(id.ToString());
        if (employee is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
            return BadRequest(new { message = "Employee name must contain at least 2 characters." });
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

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try { await organization.ValidateManagerAsync(id, request.ManagerId); }
        catch (InvalidOperationException e) { return BadRequest(new { message = e.Message }); }
        employee.FullName = request.FullName.Trim();
        employee.PhoneNumber = request.PhoneNumber?.Trim();
        employee.ManagerId = request.ManagerId;
        var result = await users.UpdateAsync(employee);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        if (request.Role is not null && !await users.IsInRoleAsync(employee, request.Role))
        {
            var current = await users.GetRolesAsync(employee);
            result = await users.RemoveFromRolesAsync(employee, current.Where(x => x is "Manager" or "Employee"));
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
    [RequirePermission("employees.disable")]
    public async Task<IActionResult> Status(long id, [FromBody] bool isActive)
    {
        try { return await SetStatus(id, isActive); }
        catch (Exception e) when (SqlConflicts.IsRetryable(e))
        { return Conflict(new { message = "Employee access changed concurrently. Reload and retry." }); }
    }

    private async Task<IActionResult> SetStatus(long id, bool isActive)
    {
        if (id == LeadAccess.UserId(User))
            return BadRequest(new { message = "You cannot deactivate your own account." });
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var employee = await users.FindByIdAsync(id.ToString());
        if (employee is null) return NotFound();

        if (!isActive && await users.IsInRoleAsync(employee, "Admin"))
        {
            var remaining = await (from person in db.Users
                join membership in db.UserRoles on person.Id equals membership.UserId
                join role in db.Roles on membership.RoleId equals role.Id
                where person.IsActive && person.Id != id && role.Name == "Admin"
                select person.Id).AnyAsync();
            if (!remaining) return BadRequest(new { message = "At least one active administrator must remain." });
        }
        if (isActive && employee.DepartmentId.HasValue &&
            !await db.Departments.AnyAsync(x => x.Id == employee.DepartmentId && x.IsActive))
            return BadRequest(new { message = "Activate the employee's department first." });

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
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpGet("{id:long}/summary")]
    [RequirePermission("employees.view")]
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
