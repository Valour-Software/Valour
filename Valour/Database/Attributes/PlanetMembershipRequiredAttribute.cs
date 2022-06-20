namespace Valour.Database.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class PlanetMembershipRequiredAttribute : Attribute
{
    public string planetRouteName;

    public PlanetMembershipRequiredAttribute(string planetRouteName)
    {
        this.planetRouteName = planetRouteName;
    }
}
