namespace BusinessFlow.Api.Models;

public sealed class WebsiteLeadSubmission
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public string SubmissionHash { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
