using Valour.Shared.Authorization;

namespace Valour.Server.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class PlanetMembershipRequiredAttribute : Attribute
{
    public readonly string planetRouteName;
    public readonly PlanetPermissionsEnum[] permissions;

    public PlanetMembershipRequiredAttribute(string planetRouteName = "planetId", params PlanetPermissionsEnum[] permissions)
    {
        this.planetRouteName = planetRouteName;
        this.permissions = permissions;
    }

    public PlanetMembershipRequiredAttribute() : this("planetId") { }
}
