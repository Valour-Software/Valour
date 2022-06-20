using Valour.Shared.Authorization;

namespace Valour.Database.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class UserPermissionsRequiredAttribute : Attribute
{
    public UserPermission[] permissions;

    public UserPermissionsRequiredAttribute(params UserPermission[] permissions)
    {
        this.permissions = permissions;
    }
}
