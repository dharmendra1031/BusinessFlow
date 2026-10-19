namespace BusinessFlow.Api.Models;

public sealed class ProjectMilestone
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public DevelopmentProject Project { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Planned";
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ProjectSprint
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public DevelopmentProject Project { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public string Status { get; set; } = "Planned";
    public DateOnly StartDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
