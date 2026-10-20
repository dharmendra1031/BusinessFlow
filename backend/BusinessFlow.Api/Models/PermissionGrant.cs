using System.Text.Json.Serialization;

namespace BusinessFlow.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<DataScope>))]
public enum DataScope { Self, DirectReports, ReportingSubtree, Team, Department, Organization }

public sealed class RolePermissionGrant
{
    public long Id { get; set; }
    public long RoleId { get; set; }
    public string Permission { get; set; } = string.Empty;
    public DataScope Scope { get; set; }
}

public sealed class UserPermissionRestriction
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Permission { get; set; } = string.Empty;
}
