using System.ComponentModel.DataAnnotations;

namespace LeadManagement.Api.Contracts;

public sealed record LoginRequest([Required] string Email, [Required] string Password);

public sealed record CreateEmployeeRequest(
    [Required, MaxLength(150)] string FullName,
    [Required, EmailAddress] string Email,
    [Required] string Password,
    [Required] string Role,
    long? ManagerId, [RegularExpression(@"^(?=(?:\\D*\\d){7,})\\+?[0-9() .-]{7,30}$", ErrorMessage = "Enter a valid phone number with at least 7 digits.")] string? PhoneNumber = null);

public sealed record LeadRequest(
    [Required, MaxLength(150)] string Name,
    [Required, MaxLength(30), [RegularExpression(@"^(?=(?:\\D*\\d){7,})\\+?[0-9() .-]{7,30}$", ErrorMessage = "Enter a valid phone number with at least 7 digits.")]] string Phone,
    [EmailAddress] string? Email,
    [MaxLength(200)] string? CompanyName,
    [MaxLength(2000)] string? Requirement,
    long SourceId = 1,
    [MaxLength(500)] string? Address = null,
    [MaxLength(150)] string? LeadGivenBy = null,
    [Range(typeof(decimal), "0", "999999999999999.999")] decimal? ProjectAmount = null,
    long? StatusId = null);

public sealed record AssignLeadRequest(long UserId);
public sealed record ChangeLeadStatusRequest(long StatusId);
public sealed record CreateFollowUpRequest(DateTime DueAtUtc, [Required, MaxLength(2000)] string Notes);

public sealed record UpdateEmployeeRequest(
    [Required, MaxLength(150)] string FullName,
    [RegularExpression(@"^(?=(?:\\D*\\d){7,})\\+?[0-9() .-]{7,30}$", ErrorMessage = "Enter a valid phone number with at least 7 digits.")] string? PhoneNumber,
    string? Role,
    long? ManagerId);

public sealed record ResetEmployeePasswordRequest([Required] string NewPassword);
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);
public sealed record UpdateFollowUpRequest(DateTime DueAtUtc, [Required, MaxLength(2000)] string Notes);
