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
    [Required] string RowVersion,
    [MaxLength(150)] string? ClientResponseBy = null,
    [MaxLength(500)] string? ClientResponseReference = null);

public sealed record ReviewQuotationRequest(
    bool Approve,
    [Required, MinLength(10), MaxLength(500)] string Comment,
    [Required] string RowVersion);

public sealed record CreateSalesContractRequest(
    [Required, MinLength(10), MaxLength(2000)] string Terms,
    [Required] string RowVersion);

public sealed record SignSalesContractRequest(
    [Required, MinLength(2), MaxLength(150)] string SignedBy,
    [Required, MinLength(5), MaxLength(500)] string SignatureReference,
    DateOnly SignedOn,
    [Required] string RowVersion);

public sealed record SubmitQuotationRequest([Required] string RowVersion);
