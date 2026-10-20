namespace BusinessFlow.Api.Services;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequirePermissionAttribute(string code) : Attribute
{
    public string Code { get; } = code;
}
