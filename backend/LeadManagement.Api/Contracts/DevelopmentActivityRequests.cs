using System.ComponentModel.DataAnnotations;

namespace LeadManagement.Api.Contracts;

public sealed record CreateProjectBugRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Title,
    [MaxLength(2000)] string? Description,
    [Required] string Severity,
    long? TaskId,
    long? AssignedToId);

public sealed record UpdateProjectBugRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Title,
    [MaxLength(2000)] string? Description,
    [Required] string Severity,
    [Required] string Status,
    long? TaskId,
    long? AssignedToId);

public sealed record ChangeProjectBugStatusRequest([Required] string Status);

public sealed record CreateDeveloperWorklogRequest(
    DateOnly WorkDate,
    [Range(typeof(decimal), "0.25", "24")] decimal Hours,
    [Required, StringLength(1000, MinimumLength = 2)] string Description,
    long? TaskId);
