using Microsoft.AspNetCore.Identity;

namespace BusinessFlow.Api.Models;

public sealed class ApplicationUser : IdentityUser<long>
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public long? ManagerId { get; set; }
    public long? DepartmentId { get; set; }
}

public sealed class LeadSource
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class LeadStatus
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class Lead
{
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? CompanyName { get; set; }
    public string? CustomerCode { get; set; }
    public string? Address { get; set; }
    public string? LeadGivenBy { get; set; }
    public decimal? ProjectAmount { get; set; }
    public DateTime? LastFollowUpDate { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public long? ModifiedByUserId { get; set; }
    public string? Requirement { get; set; }
    public long SourceId { get; set; }
    public LeadSource Source { get; set; } = null!;
    public long StatusId { get; set; } = 1;
    public LeadStatus Status { get; set; } = null!;
    public long? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LeadAssignment
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public long? PreviousUserId { get; set; }
    public long AssignedToId { get; set; }
    public long AssignedById { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LeadStatusChange
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public long OldStatusId { get; set; }
    public long NewStatusId { get; set; }
    public long ChangedById { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LeadFollowUp
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public long AssignedToId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime DueAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public long RecordId { get; set; }
    public string? ChangedFields { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
