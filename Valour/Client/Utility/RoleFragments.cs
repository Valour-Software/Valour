using Microsoft.AspNetCore.Components;
using Valour.Sdk.Models;

namespace Valour.Client.Utility;

public static class RoleFragments
{
    public static RenderFragment<PlanetRole> RolePill => role => __builder =>
    {
        __builder.OpenComponent<Valour.Client.Components.UI.RolePill>(0);
        __builder.AddAttribute(1, "Role", role);
        __builder.CloseComponent();
    };
}
