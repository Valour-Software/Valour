using Valour.Shared.Authorization;

namespace Valour.Server.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class CategoryChannelPermsRequiredAttribute : Attribute
{
    public readonly CategoryPermissionsEnum[] permissions;
    public readonly string categoryRouteName;

    public CategoryChannelPermsRequiredAttribute(string categoryRouteName, params CategoryPermissionsEnum[] permissions)
    {
        this.permissions = permissions;
        this.categoryRouteName = categoryRouteName;
    }

    public CategoryChannelPermsRequiredAttribute(params CategoryPermissionsEnum[] permissions) : this("id", permissions) { }
}
