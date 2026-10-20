using System.ComponentModel.DataAnnotations;

namespace BusinessFlow.Api.Contracts;

public sealed record SaveDepartmentRequest([Required, StringLength(150, MinimumLength = 2)] string Name, bool IsActive = true);
public sealed record SaveTeamRequest([Required, StringLength(150, MinimumLength = 2)] string Name, long DepartmentId, bool IsActive = true);
public sealed record TransferEmployeeRequest(long? DepartmentId, [Required, MaxLength(200)] long[] TeamIds);
public sealed record SetManagerRequest(long? ManagerId);
