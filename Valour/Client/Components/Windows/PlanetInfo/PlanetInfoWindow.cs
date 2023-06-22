using Valour.Api.Models;
using Valour.Client.Windows;

namespace Valour.Client.Components.Windows.PlanetInfo;

public class PlanetInfoWindow : ClientWindow
{
    public Planet Planet { get; set; }
    
    public override Type GetComponentType() =>
        typeof(PlanetInfoWindowComponent);

    public PlanetInfoWindow(Planet planet)
    {
        this.Planet = planet;
    }
}