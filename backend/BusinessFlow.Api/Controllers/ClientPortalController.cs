using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

public sealed record ClientEmailRequest(string? Email);
public sealed record ClientCodeRequest(string? Email, string? Code);

[ApiController]
[Route("api/client")]
[Authorize(AuthenticationSchemes = "ClientPortal")]
[EnableRateLimiting("client-login")]
public sealed class ClientPortalController(
    AppDbContext db, IConfiguration config, IClientOtpSender sender) : ControllerBase
{
    private static readonly IActionResult GenericAccepted = new AcceptedResult();
    private static readonly IActionResult InvalidCode =
        new UnauthorizedObjectResult(new { message = "Invalid or expired code." });

    private bool Enabled() =>
        config.GetValue<bool>("ClientPortal:Enabled") &&
        Encoding.UTF8.GetByteCount(config["ClientPortal:OtpPepper"] ?? "") >= 32 &&
        sender.IsConfigured;

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email.Trim()))
            return null;
        return email.Trim().ToUpperInvariant();
    }

    private string Hash(long accountId, string code)
    {
        var secret = Encoding.UTF8.GetBytes(config["ClientPortal:OtpPepper"]!);
        var input = Encoding.UTF8.GetBytes(accountId.ToString(CultureInfo.InvariantCulture) + ":" + code);
        return Convert.ToHexString(HMACSHA256.HashData(secret, input));
    }

    private IQueryable<ClientPortalAccount> Eligible(string email) =>
        from account in db.ClientPortalAccounts
        join contact in db.CustomerContacts on account.ContactId equals contact.Id
        join customer in db.Customers on account.CustomerId equals customer.Id
        where account.IsActive && account.Email == email &&
              contact.CustomerId == account.CustomerId && contact.IsActive &&
              contact.Email != null && contact.Email.Trim().ToUpper() == account.Email &&
              customer.IsActive && customer.MergedIntoCustomerId == null
        select account;

    [AllowAnonymous]
    [HttpPost("auth/request-code")]
    public async Task<IActionResult> RequestCode(ClientEmailRequest request, CancellationToken ct)
    {
        if (!Enabled()) return StatusCode(503, new { message = "Client sign-in is unavailable." });
        var email = NormalizeEmail(request.Email);
        if (email is null) return BadRequest(new { message = "Enter a valid email address." });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await Eligible(email).SingleOrDefaultAsync(ct);
        if (account is null) return GenericAccepted;

        var now = DateTime.UtcNow;
        var hourlyRequests = await db.ClientLoginChallenges.CountAsync(
            x => x.AccountId == account.Id && x.CreatedAtUtc >= now.AddHours(-1), ct);
        if (hourlyRequests >= 5) return GenericAccepted;
        var last = await db.ClientLoginChallenges
            .Where(x => x.AccountId == account.Id)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (last is not null && last.CreatedAtUtc > now.AddMinutes(-1))
            return GenericAccepted;

        var open = await db.ClientLoginChallenges
            .Where(x => x.AccountId == account.Id && x.UsedAtUtc == null).ToListAsync(ct);
        foreach (var previous in open) previous.UsedAtUtc = now;

        var code = RandomNumberGenerator.GetInt32(0, 100000000).ToString("D8", CultureInfo.InvariantCulture);
        var challenge = new ClientLoginChallenge
        {
            AccountId = account.Id,
            CodeHash = Hash(account.Id, code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10)
        };
        db.ClientLoginChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        try { await sender.SendAsync(account.Email, code, ct); }
        catch (Exception e) when (e is System.Net.Mail.SmtpException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            challenge.UsedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return GenericAccepted;
    }

    [AllowAnonymous]
    [HttpPost("auth/verify-code")]
    public async Task<IActionResult> VerifyCode(ClientCodeRequest request, CancellationToken ct)
    {
        if (!Enabled()) return StatusCode(503, new { message = "Client sign-in is unavailable." });
        var email = NormalizeEmail(request.Email);
        if (email is null || !Regex.IsMatch(request.Code ?? "", @"^[0-9]{8}$"))
            return InvalidCode;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await Eligible(email).SingleOrDefaultAsync(ct);
        if (account is null) return InvalidCode;

        var challenge = await db.ClientLoginChallenges.Where(x => x.AccountId == account.Id)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        if (challenge is null || challenge.UsedAtUtc is not null ||
            challenge.ExpiresAtUtc <= now || challenge.AttemptCount >= 5 ||
            account.RevokedAtUtc.HasValue && challenge.CreatedAtUtc <= account.RevokedAtUtc.Value)
            return InvalidCode;

        var hash = Convert.FromHexString(Hash(account.Id, request.Code!));
        var stored = Convert.FromHexString(challenge.CodeHash);
        if (!CryptographicOperations.FixedTimeEquals(hash, stored))
        {
            challenge.AttemptCount++;
            if (challenge.AttemptCount >= 5) challenge.UsedAtUtc = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return InvalidCode;
        }

        challenge.UsedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var claims = new[]
        {
            new Claim("client_account", account.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim("client_version", account.SessionVersion.ToString(CultureInfo.InvariantCulture))
        };
        await HttpContext.SignInAsync("ClientPortal", new ClaimsPrincipal(
            new ClaimsIdentity(claims, "ClientPortal")));
        return Ok(new { signedIn = true });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var id = long.Parse(User.FindFirst("client_account")!.Value, CultureInfo.InvariantCulture);
        var profile = await (from account in db.ClientPortalAccounts.AsNoTracking()
                             join customer in db.Customers.AsNoTracking() on account.CustomerId equals customer.Id
                             join contact in db.CustomerContacts.AsNoTracking() on account.ContactId equals contact.Id
                             where account.Id == id
                             select new { customerName = customer.Name, contactName = contact.Name,
                                          email = account.Email })
            .SingleOrDefaultAsync(ct);
        return profile is null ? Unauthorized() : Ok(profile);
    }

    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var id = long.Parse(User.FindFirst("client_account")!.Value, CultureInfo.InvariantCulture);
        var account = await db.ClientPortalAccounts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (account is not null)
        {
            account.SessionVersion++;
            await db.SaveChangesAsync(ct);
        }
        await HttpContext.SignOutAsync("ClientPortal");
        return NoContent();
    }
}
