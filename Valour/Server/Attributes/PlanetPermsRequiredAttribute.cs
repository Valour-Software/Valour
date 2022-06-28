using Valour.Shared.Authorization;

namespace Valour.Server.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class PlanetPermsRequiredAttribute : Attribute
{
    public readonly string planetRouteName;
    public readonly PlanetPermissionsEnum[] permissions;

    public PlanetPermsRequiredAttribute(string planetRouteName, params PlanetPermissionsEnum[] permissions)
    {
        this.planetRouteName = planetRouteName;
        this.permissions = permissions;
    }

    public PlanetPermsRequiredAttribute(params PlanetPermissionsEnum[] permissions) : this("planetId", permissions) { }
}
