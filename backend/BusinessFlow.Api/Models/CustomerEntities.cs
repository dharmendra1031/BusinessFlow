using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Models;

public sealed class Customer
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? LegalName { get; set; }
    public string? Gstin { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountry { get; set; }
    public string? NormalizedPhone { get; private set; }
    public bool IsActive { get; set; } = true;
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CustomerContact
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
