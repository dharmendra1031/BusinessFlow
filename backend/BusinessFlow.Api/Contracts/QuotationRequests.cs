using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Contracts;

public sealed record QuotationDetails(
    [Required, MinLength(2), MaxLength(150)] string Title,
    [MaxLength(2000)] string? Scope,
    decimal Amount,
    [Required, RegularExpression("^[a-zA-Z]{3}$")] string Currency,
    DateOnly? ValidUntil,
    [MaxLength(1000)] string? PaymentTerms,
    List<QuotationLineRequest>? Items = null);

public sealed record QuotationLineRequest(
    [Required, MinLength(2), MaxLength(250)] string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountRate = 0,
    decimal TaxRate = 0);

public sealed record CreateQuotationRequest(
    [Range(1, long.MaxValue)] long LeadId,
    [Required] QuotationDetails Details);

public sealed record ReviseQuotationRequest(
    [Required] QuotationDetails Details,
    [Required] string RowVersion);

public sealed record ChangeQuotationStatusRequest(
    [Required] string Status,
    [Required] string RowVersion);

public sealed record SubmitQuotationRequest([Required] string RowVersion);
