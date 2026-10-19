namespace LeadManagement.Api.Models;

public sealed class RolePermissionOverride
{
    public long Id { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
