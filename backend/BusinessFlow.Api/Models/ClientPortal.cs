using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Models;

public sealed class ClientPortalAccount
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long ContactId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public long CreatedById { get; set; }
    public int SessionVersion { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ClientLoginChallenge
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
