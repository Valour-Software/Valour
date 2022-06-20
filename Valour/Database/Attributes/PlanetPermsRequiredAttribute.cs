using Valour.Shared.Authorization;

namespace Valour.Database.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class PlanetPermsRequiredAttribute : Attribute
{
    public string planetRouteName;
    public PlanetPermissionsEnum[] permissions;

    public PlanetPermsRequiredAttribute(string planetRouteName, params PlanetPermissionsEnum[] permissions)
    {
        this.planetRouteName = planetRouteName;
        this.permissions = permissions;
    }

    public PlanetPermsRequiredAttribute(params PlanetPermissionsEnum[] permissions) : this("planet_id", permissions) { }
}
