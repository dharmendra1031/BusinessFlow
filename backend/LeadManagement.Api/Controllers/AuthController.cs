using LeadManagement.Api.Contracts;
using LeadManagement.Api.Models;
using LeadManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LeadManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users, TokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user))
            return Unauthorized(new { message = "Invalid login." });

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return Unauthorized(new { message = "Invalid login." });
        }

        await users.ResetAccessFailedCountAsync(user);
        var roles = await users.GetRolesAsync(user);
        var (token, expiresAtUtc) = tokens.Create(user, roles);
        return Ok(new { token, expiresAtUtc, user = new { user.Id, user.FullName, user.Email, roles } });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await users.FindByIdAsync(LeadAccess.UserId(User).ToString());
        if (user is null || !user.IsActive) return Unauthorized();
        return Ok(new { user.Id, user.FullName, user.Email, roles = await users.GetRolesAsync(user) });
    }
}
