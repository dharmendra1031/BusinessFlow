using System.ComponentModel.DataAnnotations;

namespace LeadManagement.Api.Contracts;

public sealed record CreateProjectRequest(
    [Range(1, long.MaxValue)] long LeadId,
    [Required, MinLength(2), MaxLength(150)] string Title,
    [MaxLength(2000)] string? Description,
    [Range(1, long.MaxValue)] long OwnerId,
    DateOnly? StartDate,
    DateOnly? DueDate);

public sealed record UpdateProjectRequest(
    [Required, MinLength(2), MaxLength(150)] string Title,
    [MaxLength(2000)] string? Description,
    [Required] string Status,
    [Range(1, long.MaxValue)] long OwnerId,
    DateOnly? StartDate,
    DateOnly? DueDate);

public sealed record CreateDevelopmentTaskRequest(
    [Required, MinLength(2), MaxLength(150)] string Title,
    [MaxLength(2000)] string? Description,
    [Required] string Priority,
    long? AssignedToId,
    DateOnly? DueDate);

public sealed record UpdateDevelopmentTaskRequest(
    [Required, MinLength(2), MaxLength(150)] string Title,
    [MaxLength(2000)] string? Description,
    [Required] string Priority,
    long? AssignedToId,
    DateOnly? DueDate);

public sealed record UpdateDevelopmentTaskStatusRequest([Required] string Status);
