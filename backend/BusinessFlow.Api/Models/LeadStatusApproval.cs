using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Models;

public sealed class LeadStatusApproval
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public long RequestedStatusId { get; set; }
    public long RequestedById { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] LeadVersion { get; set; } = [];
    public string State { get; set; } = "Pending";
    public long? DecidedById { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? Comment { get; set; }
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
