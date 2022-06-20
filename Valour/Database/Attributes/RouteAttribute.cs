using System.Web.Mvc;

namespace Valour.Database.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class ValourRouteAttribute : Attribute
{
    public readonly string route;
    public readonly HttpVerbs method;

    public ValourRouteAttribute(HttpVerbs method, string route = null)
    {
        this.route = route;
        this.method = method;
    }
}
