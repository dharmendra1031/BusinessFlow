namespace BusinessFlow.Api.Models;

public sealed class DevelopmentProject
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Planning";
    public long OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;
    public long CreatedById { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<DevelopmentTask> Tasks { get; set; } = new List<DevelopmentTask>();
}

public sealed class DevelopmentTask
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public DevelopmentProject Project { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Todo";
    public string Priority { get; set; } = "Medium";
    public long? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }
    public DateOnly? DueDate { get; set; }
    public long? MilestoneId { get; set; }
    public ProjectMilestone? Milestone { get; set; }
    public long? SprintId { get; set; }
    public ProjectSprint? Sprint { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
