using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/password")]
public sealed class PasswordsController(UserManager<ApplicationUser> users, AppDbContext db, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("change")]
    public async Task<IActionResult> Change(ChangePasswordRequest request)
    {
        var user = await users.FindByIdAsync(LeadAccess.UserId(User).ToString());
        if (user is null) return Unauthorized();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(x => x.Description) });
        Response.Cookies.Delete("CRM.Session", new CookieOptions
        {
            Path = "/", Secure = !environment.IsDevelopment(),
            HttpOnly = true, SameSite = SameSiteMode.Strict
        });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id, Action = "ChangePassword",
            TableName = "Users", RecordId = user.Id
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("reset/{id:long}")]
    [RequirePermission("employees.reset-password")]
    public async Task<IActionResult> Reset(long id, ResetEmployeePasswordRequest request)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(x => x.Description) });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = "ResetPassword",
            TableName = "Users", RecordId = id
        });
        await db.SaveChangesAsync();
        return NoContent();
    }
}
