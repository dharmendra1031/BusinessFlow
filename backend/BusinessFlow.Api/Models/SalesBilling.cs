using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Models;

public sealed class SalesInvoice
{
    public long Id { get; set; }
    public long ContractId { get; set; }
    public string Type { get; set; } = "Milestone";
    public string Description { get; set; } = string.Empty;
    public string BillToName { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal CreditedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = "Issued";
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SalesPayment
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public decimal RefundedAmount { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SalesCreditNote
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly IssuedOn { get; set; }
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SalesRefund
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public long PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateOnly RefundedOn { get; set; }
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FinancialAdjustmentApproval
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long? PaymentId { get; set; }
    public string? Method { get; set; }
    public string? Reference { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly EffectiveOn { get; set; }
    public byte[] InvoiceVersion { get; set; } = [];
    public string State { get; set; } = "Pending";
    public long RequestedById { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public long? DecidedById { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionComment { get; set; }
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
