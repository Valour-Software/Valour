namespace Valour.Server.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class PlanetMembershipRequiredAttribute : Attribute
{
    public readonly string planetRouteName;

    public PlanetMembershipRequiredAttribute(string planetRouteName)
    {
        this.planetRouteName = planetRouteName;
    }

    public PlanetMembershipRequiredAttribute() : this("planetId") { }
}
