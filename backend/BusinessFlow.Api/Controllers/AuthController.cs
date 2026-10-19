using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users, TokenService tokens,
    IAntiforgery antiforgery, IWebHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
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
        Response.Cookies.Append("CRM.Session", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero)
        });
        return Ok(new
        {
            token = environment.IsDevelopment() ? token : null,
            expiresAtUtc,
            user = new { user.Id, user.FullName, user.Email, roles }
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await users.FindByIdAsync(LeadAccess.UserId(User).ToString());
        if (user is null || !user.IsActive) return Unauthorized();
        return Ok(new { user.Id, user.FullName, user.Email, roles = await users.GetRolesAsync(user),
            csrfToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("CRM.Session", new CookieOptions
        {
            Path = "/", Secure = !environment.IsDevelopment(),
            HttpOnly = true, SameSite = SameSiteMode.Strict
        });
        return NoContent();
    }
}
