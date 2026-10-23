namespace BusinessFlow.Api.Models;

// One optional source-to-team rule. Disabled or unavailable rules use the normal owner fallback.
public sealed class LeadRoutingRule
{
    public long Id { get; set; }
    public long SourceId { get; set; }
    public long TeamId { get; set; }
    public int MaxOpenLeads { get; set; } = 20;
    public bool IsActive { get; set; }
}
