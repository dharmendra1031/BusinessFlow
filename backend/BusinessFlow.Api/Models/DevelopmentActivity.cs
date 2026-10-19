namespace BusinessFlow.Api.Models;

public sealed class ProjectBug
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public DevelopmentProject Project { get; set; } = null!;
    public long? TaskId { get; set; }
    public DevelopmentTask? Task { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Severity { get; set; } = "Medium";
    public string Status { get; set; } = "Open";
    public long? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }
    public long ReportedById { get; set; }
    public ApplicationUser ReportedBy { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
}

public sealed class DeveloperWorklog
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public DevelopmentProject Project { get; set; } = null!;
    public long? TaskId { get; set; }
    public DevelopmentTask? Task { get; set; }
    public long UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
