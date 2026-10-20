namespace BusinessFlow.Api.Models;

public sealed class Department
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class Team
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TeamMembership
{
    public long Id { get; set; }
    public long TeamId { get; set; }
    public long UserId { get; set; }
}
