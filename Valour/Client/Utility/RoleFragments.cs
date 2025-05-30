using Microsoft.AspNetCore.Components;
using Valour.Sdk.Models;

namespace Valour.Client.Utility;

public static class RoleFragments
{
    public static RenderFragment<PlanetRole> RolePill => role => __builder =>
    {
        __builder.OpenElement(0, "span");
        __builder.AddAttribute(1, "class", "role-pill");
        __builder.AddAttribute(2, "style", $"border-color:{role.Color}");
        __builder.AddContent(3, role.Name);
        __builder.CloseElement();
    };
}
