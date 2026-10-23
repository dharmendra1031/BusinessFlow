namespace BusinessFlow.Api.Models;

/// <summary>Opt-in, per-source routing policy. A disabled or absent policy preserves existing assignment behavior.</summary>
public sealed class LeadRoutingRule
{
    public long Id { get; set; }
    public long SourceId { get; set; }
    public long TeamId { get; set; }
    public int MaxOpenLeads { get; set; } = 30;
    public bool IsActive { get; set; }
}
