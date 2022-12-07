namespace Valour.Server.EndpointFilters.Attributes;


[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class ValourRouteAttribute : Attribute
{
    public readonly string route;
    public readonly string baseRoute;
    public readonly HttpVerbs method;

    public ValourRouteAttribute(HttpVerbs method, string route = null, string prefix = null)
    {
        this.route = route;
        this.method = method;
        this.baseRoute = prefix;
    }
}
