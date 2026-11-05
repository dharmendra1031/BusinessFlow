using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Contracts;

public sealed record IssueCreditNoteRequest(
    decimal Amount,
    [Required, MinLength(10), MaxLength(500)] string Reason,
    DateOnly IssuedOn,
    [Required] string InvoiceRowVersion);

public sealed record RecordRefundRequest(
    [Range(1, long.MaxValue)] long PaymentId,
    decimal Amount,
    [Required] string Method,
    [Required, MinLength(5), MaxLength(100)] string Reference,
    [Required, MinLength(10), MaxLength(500)] string Reason,
    DateOnly RefundedOn,
    [Required] string InvoiceRowVersion);
