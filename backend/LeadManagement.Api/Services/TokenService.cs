using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LeadManagement.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace LeadManagement.Api.Services;

public sealed class TokenService(IConfiguration configuration)
{
    public (string Token, DateTime ExpiresAtUtc) Create(ApplicationUser user, IList<string> roles)
    {
        var expiresAt = DateTime.UtcNow.AddHours(8);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("name", user.FullName),
            new("security_stamp", user.SecurityStamp ?? string.Empty)
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var secret = configuration["Jwt:Key"]!;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"], audience: configuration["Jwt:Audience"],
            claims: claims, expires: expiresAt, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
