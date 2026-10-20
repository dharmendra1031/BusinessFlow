using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Contracts;

public sealed record RequestLeadApproval(long StatusId, [Required] string RowVersion);
public sealed record DecideLeadApproval(bool Approve, [MaxLength(500)] string? Comment);
