namespace BusinessFlow.Api.Models;

/// <summary>Reminder delivery/read state is per current employee, not per customer lead.</summary>
public sealed class FollowUpReminderState
{
    public long Id { get; set; }
    public long FollowUpId { get; set; }
    public long UserId { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public DateTime? SnoozedUntilUtc { get; set; }
}
