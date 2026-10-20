using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Contracts;

public sealed record LoginRequest([Required, EmailAddress, MaxLength(256)] string Email, [Required] string Password);

public sealed record CreateEmployeeRequest(
    [Required, MinLength(2), MaxLength(150)] string FullName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required] string Role,
    long? ManagerId, [MaxLength(30), RegularExpression(@"^(?=(?:\D*\d){7,15}\D*$)\+?[0-9() .-]{7,30}$", ErrorMessage = "Enter a valid phone number with 7 to 15 digits.")] string? PhoneNumber = null);

public sealed record LeadRequest(
    [Required, MinLength(2), MaxLength(150)] string Name,
    [Required, MaxLength(30), RegularExpression(@"^(?=(?:\D*\d){7,15}\D*$)\+?[0-9() .-]{7,30}$", ErrorMessage = "Enter a valid phone number with 7 to 15 digits.")] string Phone,
    [EmailAddress, MaxLength(256)] string? Email,
    [MaxLength(200)] string? CompanyName,
    [MaxLength(2000)] string? Requirement,
    long SourceId = 1,
    [MaxLength(500)] string? Address = null,
    [MaxLength(150)] string? LeadGivenBy = null,
    [Range(typeof(decimal), "0", "999999999999999.999")] decimal? ProjectAmount = null,
    long? StatusId = null, string? RowVersion = null, [MaxLength(500)] string? OverrideReason = null);

public sealed record AssignLeadRequest(long UserId, string? RowVersion = null);
public sealed record ChangeLeadStatusRequest(long StatusId, string? RowVersion = null, [MaxLength(500)] string? OverrideReason = null);
public sealed record CreateFollowUpRequest(DateTime DueAtUtc, [Required, MaxLength(2000)] string Notes);

public sealed record UpdateEmployeeRequest(
    [Required, MinLength(2), MaxLength(150)] string FullName,
    [MaxLength(30), RegularExpression(@"^(?=(?:\D*\d){7,15}\D*$)\+?[0-9() .-]{7,30}$", ErrorMessage = "Enter a valid phone number with 7 to 15 digits.")] string? PhoneNumber,
    string? Role,
    long? ManagerId);

public sealed record ResetEmployeePasswordRequest([Required, StringLength(128, MinimumLength = 12)] string NewPassword);
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required, StringLength(128, MinimumLength = 12)] string NewPassword);
public sealed record UpdateFollowUpRequest(DateTime DueAtUtc, [Required, MaxLength(2000)] string Notes);
