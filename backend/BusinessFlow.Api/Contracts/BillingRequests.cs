using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Contracts;

public sealed record CreateInvoiceRequest(
    [Range(1, long.MaxValue)] long ContractId,
    [Required] string Type,
    [Required, MinLength(3), MaxLength(250)] string Description,
    decimal Amount,
    DateOnly DueDate,
    [Required] string ContractRowVersion);

public sealed record ReceiveInvoicePaymentRequest(
    decimal Amount,
    [Required] string Method,
    [Required, MinLength(5), MaxLength(100)] string Reference,
    DateOnly ReceivedOn,
    [Required] string InvoiceRowVersion);

public sealed record VoidInvoiceRequest(
    [Required, MinLength(10), MaxLength(500)] string Reason,
    [Required] string InvoiceRowVersion);
