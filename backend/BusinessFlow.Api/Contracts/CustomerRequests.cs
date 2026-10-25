using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Contracts;

public sealed record SaveCustomerRequest(
    [Required, MinLength(2), MaxLength(200)] string Name,
    [EmailAddress, MaxLength(256)] string? Email = null,
    [MaxLength(30), RegularExpression(@"^(?=(?:\D*\d){7,15}\D*$)\+?[0-9() .-]{7,30}$")] string? Phone = null,
    [MaxLength(500)] string? Address = null,
    bool IsActive = true,
    string? RowVersion = null);

public sealed record SaveCustomerContactRequest(
    [Required, MinLength(2), MaxLength(150)] string Name,
    [MaxLength(100)] string? Designation = null,
    [EmailAddress, MaxLength(256)] string? Email = null,
    [MaxLength(30), RegularExpression(@"^(?=(?:\D*\d){7,15}\D*$)\+?[0-9() .-]{7,30}$")] string? Phone = null,
    bool IsPrimary = false,
    bool IsActive = true);

public sealed record LinkCustomerLeadRequest([Required] string RowVersion);
