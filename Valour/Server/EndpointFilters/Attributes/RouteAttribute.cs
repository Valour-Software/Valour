namespace Valour.Server.EndpointFilters.Attributes;


[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public class ValourRouteAttribute : Attribute
{
    public readonly string Route;
    public readonly HttpVerbs Method;

    public ValourRouteAttribute(HttpVerbs method, string route)
    {
        this.Route = route;
        this.Method = method;
    }
}
