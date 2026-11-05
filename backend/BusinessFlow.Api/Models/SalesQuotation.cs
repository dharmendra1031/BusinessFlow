using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Models;

public sealed class SalesQuotation
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public string Status { get; set; } = "Draft";
    public int CurrentRevision { get; set; } = 1;
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SalesQuotationRevision
{
    public long Id { get; set; }
    public long QuotationId { get; set; }
    public int Revision { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public decimal Amount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public ICollection<SalesQuotationLineItem> Items { get; set; } = new List<SalesQuotationLineItem>();
    public string Currency { get; set; } = "INR";
    public DateOnly? ValidUntil { get; set; }
    public string? PaymentTerms { get; set; }
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SalesQuotationLineItem
{
    public long Id { get; set; }
    public long RevisionId { get; set; }
    public int Position { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal TaxRate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}
