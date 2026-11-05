using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Contracts;

public sealed record FinancialDecisionRequest(
    bool Approve,
    [Required, MinLength(10), MaxLength(500)] string Comment,
    [Required] string RowVersion);
