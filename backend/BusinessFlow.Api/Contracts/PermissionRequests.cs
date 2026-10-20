using System.ComponentModel.DataAnnotations;
using BusinessFlow.Api.Models;

namespace BusinessFlow.Api.Contracts;

public sealed record SaveRoleRequest([Required, StringLength(80, MinimumLength = 2)] string Name);
public sealed record SaveGrantRequest(DataScope? Scope);
public sealed record SetRoleMemberRequest(bool Assigned);
public sealed record SetRestrictionRequest([Required] bool? Denied);
