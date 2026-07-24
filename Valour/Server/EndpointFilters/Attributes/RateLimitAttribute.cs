namespace Valour.Server.EndpointFilters.Attributes;

/// <summary>
/// Applies a named rate-limiting policy to a ValourRoute. Policies are defined
/// in <see cref="Valour.Server.Utilities.RateLimitPolicies"/> and registered at
/// startup.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class RateLimitAttribute : Attribute
{
    public readonly string Policy;

    public RateLimitAttribute(string policy)
    {
        Policy = policy;
    }
}
