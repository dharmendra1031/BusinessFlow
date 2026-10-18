namespace LeadManagement.Api.Models;

public sealed class DevelopmentProject
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Planned";
    public long OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;
    public long CreatedById { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DevelopmentTask> Tasks { get; set; } = [];
}

public sealed class DevelopmentTask
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public DevelopmentProject Project { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Todo";
    public long? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }
    public DateTime? DueDate { get; set; }
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
