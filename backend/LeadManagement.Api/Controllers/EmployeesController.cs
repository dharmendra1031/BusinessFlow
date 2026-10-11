using LeadManagement.Api.Contracts;
using LeadManagement.Api.Data;
using LeadManagement.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/employees")]
public sealed class EmployeesController(AppDbContext db, UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var query = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (!User.IsInRole("Admin"))
        {
            if (!User.IsInRole("Manager")) return Forbid();
            var managerId = Services.LeadAccess.UserId(User);
            query = query.Where(u => u.ManagerId == managerId || u.Id == managerId);
        }

        var result = await query.OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Email, u.ManagerId }).ToListAsync();
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateEmployeeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { message = "Employee name is required." });
        if (request.Role is not ("Manager" or "Employee"))
            return BadRequest(new { message = "Role must be Manager or Employee." });
        if (request.ManagerId is long managerId)
        {
            var manager = await users.FindByIdAsync(managerId.ToString());
            if (manager is null || !manager.IsActive || !await users.IsInRoleAsync(manager, "Manager"))
                return BadRequest(new { message = "Manager not found." });
        }

        var user = new ApplicationUser
        {
            FullName = request.FullName.Trim(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            ManagerId = request.ManagerId,
            LockoutEnabled = true
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return BadRequest(new { errors = created.Errors.Select(e => e.Description) });
        var assigned = await users.AddToRoleAsync(user, request.Role);
        if (!assigned.Succeeded)
            return StatusCode(500, new { message = "Could not assign employee role." });
        await transaction.CommitAsync();
        return Created($"/api/employees/{user.Id}", new { user.Id, user.FullName, user.Email });
    }
}
