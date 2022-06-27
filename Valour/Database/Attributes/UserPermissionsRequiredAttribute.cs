using Valour.Shared.Authorization;

namespace Valour.Database.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class UserPermissionsRequiredAttribute : Attribute
{
    public readonly UserPermissionsEnum[] permissions;

    public UserPermissionsRequiredAttribute(params UserPermissionsEnum[] permissions)
    {
        this.permissions = permissions;
    }
}
