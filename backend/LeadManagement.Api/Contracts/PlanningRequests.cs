using System.ComponentModel.DataAnnotations;

namespace LeadManagement.Api.Contracts;

public sealed record SaveMilestoneRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Title,
    [MaxLength(1000)] string? Description,
    [Required] string Status,
    DateOnly? DueDate);

public sealed record SaveSprintRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Title,
    [MaxLength(1000)] string? Goal,
    [Required] string Status,
    DateOnly StartDate,
    DateOnly DueDate);
