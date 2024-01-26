namespace Valour.Server.EndpointFilters.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class RateLimitAttribute: Attribute
{
    public readonly string PolicyName;
    
    public RateLimitAttribute(string policyName)
    {
        this.PolicyName = policyName;
    }
}